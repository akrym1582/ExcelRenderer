# 数式の計算

[English](formulas.md) · [対応構文・サポート関数](formulas-reference.ja.md) · [検証記録](formulas-validation.md) · [性能](formulas-performance.md) · [レビュー修正の検証](formulas-review-fixes-validation.md)

ExcelRenderer は明示指定で限定的な数式評価を行います。既定の `CachedValue` は変更せず、
古い値も含めた保存結果を使います。`Recalculate` は OpenXml アダプターを通じて、BCL のみで
実装した独立ライブラリ `ExcelRenderer.Formulas` を使います。Excel・ClosedXML・外部計算器は
呼び出しません。Excel 完全互換を保証する機能ではありません。

```csharp
var document = new ExcelRenderer.Excel.ExcelReader().Read(input,
    new ExcelRenderer.Excel.ExcelReadOptions
    {
        FormulaDisplayMode = ExcelRenderer.Excel.FormulaDisplayMode.Recalculate,
    });
```

Core では `PdfExportOptions.ReadOptions` の
`ExcelRenderer.Core.FormulaDisplayMode.Recalculate` を指定します。
本体の PDF・PNG・SVG・Markdown も既存の `ReadOptions` を使います。
既存 CLI の描画コマンドは `--formula-mode recalculate` に対応し、既定値は `cached` です。
過去の `Legacy` モードは引き続き拒否します。

## Mapping と XLSX 保存

```csharp
ExcelRenderer.Mapping.ExcelTemplateMapper.Map(template, output, data,
    new ExcelRenderer.Mapping.MappingOptions
    {
        FormulaCalculationMode = ExcelRenderer.OpenXml.FormulaCalculationMode.Recalculate,
    });
```

処理順序は **入力更新・明細行展開 → 参照書換え → 計算 → 表示書式 → 描画** です。
既定の Mapping は従来どおり値の数式を拒否します。明示的な再計算指定で対応するセル数式を
利用できます。入力値の先頭が `=` でも文字列として扱います。

既存 XLSX は `ExcelRenderer.OpenXml.WorkbookRecalculator.Recalculate(input, output)`
で全数式を計算できます。対象 `FormulaCellAddress` の列挙を渡すと、そのセルと到達依存だけを
計算します。一つのセッション内で依存結果を再利用します。入力スナップショットは不変です。
入力更新・行展開後は新しいセッションを作り、以前の計算結果を流用しません。

保存結果は Number・Text・Boolean・Excel Error を OOXML の型付きキャッシュにします。
**未評価の数式キャッシュはすべて削除します**。対象外の未対応式も古い結果を残しません。
古い計算チェーンを削除し、ブックを開いたアプリに完全再計算を要求します。このフラグは、
本エンジンが未対応式まで計算したという意味ではありません。CachedValue で再読すると
未評価セルはキャッシュ欠落として診断されます。
計算・取消し失敗は最終コピー前なら出力を変更しません。最終的な出力 I/O の失敗は、
ストリームに部分的な出力が残る場合があります。

共有数式は構文解析済みの参照から復元します。コピーでは相対座標だけを移動し、`$` 固定を
守ります。行の構造的な移動では、`$` の有無によらず参照先の位置を移動します。
文字列中の `A1` は書き換えません。名前の参照は名前として保持し、その固定範囲を行移動に
追随させます。

## 単独エンジン

```csharp
using ExcelRenderer.Formulas;

var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Invoice");
builder.SetValue(new FormulaCellAddress("Invoice", 1, 1), FormulaValue.FromNumber(3));
builder.SetValue(new FormulaCellAddress("Invoice", 1, 2), FormulaValue.FromNumber(125));
builder.SetFormula(new FormulaCellAddress("Invoice", 1, 3), "ROUND(A1*B1*1.1,0)");
var session = new FormulaEngine().CreateSession(builder.Build());
var result = session.EvaluateCell(new FormulaCellAddress("Invoice", 1, 3));
// result.Success、result.Value.Number == 413。
```

`FormulaParser.Parse` は不変 AST と UTF-16 の元位置を返します。個別の `Evaluate` /
`EvaluateCell` は独立セッションです。多数のセルには `CreateSession` を使い、依存計算を
共有してください。異なるブックやセッションに計算値を共有する static キャッシュはありません。
A1 は絶対座標 A1 を表し、origin からのオフセットではありません。

対応する40関数の引数数・例、構文、参照規則、型変換、エラー、資源上限は
[数式リファレンス](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/formulas-reference.ja.md)
にまとめています。符号・四則・べき乗・%・
文字列連結・比較、A1 の相対／絶対／混合参照、矩形／全行／全列範囲、引用シート名と
固定セル／矩形の名前を扱います。英語関数名・カンマ区切りを使い、巨大範囲は上限付きで走査します。

## 限定互換プロファイル

- 符号はべき乗より優先し `-2^2=4`、べき乗は左結合で `2^3^2=64`。
- 数値文字列は ASCII 空白を除いた invariant の符号・小数・指数だけ。日付・通貨・桁区切りは変換しません。
- 集計の参照中の文字列・真偽値は無視し、直接引数は数値文字列・真偽値を数値化します。
  Blank・空文字・省略引数を区別します。
- 文字比較は OrdinalIgnoreCase、異型は Number < Text < Boolean。連結の数値は invariant G15。
- ROUND の中間値は0から遠く、ROUNDUP は0から遠く、ROUNDDOWN は0へ丸めます。
  丸めの桁処理だけで、入力 double の invariant 最短ラウンドトリップ十進表現
  (`ToString("R", InvariantCulture)`) を整数係数と指数に分解し、整数の商・余りで
  丸めて double に戻します。num_digits は0方向へ整数化します。
  `ROUNDDOWN(0.29,2)=0.29`、`ROUNDUP(0.07,2)=0.07`、`ROUND(1.005,2)=1.01`。
  隣接 double を区別し、計算で生じた `0.30000000000000004` を `0.3` に正規化しません。
  丸め結果が非有限なら `#NUM!`。非ゼロ入力で桁数が -308 未満の場合の正の0を含め、既存の0の符号を維持します。
  Excel 内部の精度補正を完全再現する仕様ではありません。
- IF/IFERROR は選択分岐だけ評価しますが、到達した AST 全体を事前検査します。
  `IF(TRUE,1,UNKNOWN())` は UnsupportedFunction です。エンジン失敗を IFERROR で隠しません。
- AND/OR は後続エラーを短絡で消しません。COUNT は通常の Excel エラーを数えず、COUNTA は数えます。

配列・動的配列・スピル、外部／3D／構造化参照、暗黙の交差、参照の和集合／交差、R1C1、
式名・相対名、近似検索、INDIRECT/OFFSET、LET/LAMBDA、揮発性関数、VBA/UDF、
反復計算などは未対応です。到達した未対応式は診断で失敗し、保存キャッシュへフォールバックしません。
複数セル範囲の結果を黙って左上の値に変えません。

既定上限は式長8192文字、AST 16384ノード、構文深さ128、依存深さ256、数式100000セル、
セル訪問1000000回、評価2000000ステップです。`FormulaEvaluationOptions` で明示変更できます。
取消しは `OperationCanceledException`、循環は依存経路付きの診断です。

## 文字列の資源制限

```csharp
var limits = new FormulaEvaluationOptions
{
    MaxStringLength = 32767,
    MaxCachedStringCharacters = 16777216L,
};
var bounded = new FormulaEngine().CreateSession(source, limits);
// WorkbookRecalculator.Recalculate(input, output, options: limits);
// MappingOptions.FormulaEvaluationOptions と Reader の設定にも指定できます。
```

既定値は本ライブラリの資源制限で、単位は **UTF-16 コード単位** です。
Unicode の文字数や Excel のすべての Unicode 長さ規則との互換保証ではありません。
surrogate pair は2単位です。負数はセッション生成時に `ArgumentOutOfRangeException`、
0は一値上限では空の Text だけ、キャッシュ予算では空の保持 Text だけを許可します。
設定は生成時にコピーし、後の Options 変更は影響しません。
保存用の `FormulaValue.FromText` 自体にはセッション制限を持ち込みません。

`MaxStringLength` は評価した文字列リテラル、入力セルの Text、IF の選択結果、
一時的な連結結果に適用します。連結は長さの合計を long で検査し、上限内の場合だけ
結果文字列を確保します。IF の未選択分岐は評価せず、既存の静的な関数検査は維持します。
`MaxCachedStringCharacters` はセッションのセル値キャッシュに保持する Text の長さの合計です。
同一セルの再読取や results 辞書への登録は二重計上せず、同じ文字列インスタンスでも
異なるセルなら各セル分を計上します。セッションが保持しない `Evaluate(parsed)` の返却値は
累積予算に加えませんが、一値上限は適用します。

超過時は `ResourceLimitExceeded` とし、数式セル、元の Span、上限名を Feature／Message に
含めます。全文字列値は診断に含めず、IFERROR でも捕捉できません。失敗セルに未完成の
成功結果を残しません。OpenXml／Mapping の計算失敗は最終コピー前の出力先を変更しません。
呼出し元の入力データ、OpenXml スナップショット、プロセス全体のメモリや GC 後の RSS を
制限する予算ではありません。

## 配布と検証

Formulas は .NET Standard 2.1 の独立パッケージとし、OpenXml が依存します。
既存の本体・Mapping・CLI 利用者に必要な DLL を届けるため、添付の単独段階にあった
非 pack 方針を統合段階で変更しています。ローカル pack は公開やリリースを意味しません。
計算コアには project/runtime package 依存がなく、Analyzer だけは既存の開発依存を継承します。

実行した件数・コマンド・性能・未完事項は検証記録を参照してください。
Excel 実機照合は未実施です。二進丸め境界、Unicode 照合、極端な数の G15 表現、
広範な型変換の互換性は未検証であり、完全互換とは表現しません。

## 対象範囲

描画対象セルとその到達依存だけを評価します。ページ番号の選択時は、形状・寸法だけの事前計画で
対象を決め、実際のページ生成と同じ候補選択処理を使います。繰り返し見出しも対象です。
保存された行列寸法とオブジェクト範囲からページを決めるため、古いキャッシュを計算結果として
扱いません。その後の実読込で候補セルを計算し、書式化・文字計測へ進みます。
除外ページの無関係な循環や未対応式は描画を止めません。
Markdown と連続出力は、ページ出力の印刷範囲を流用せず既存の領域規則に従います。

Mapping の `RecalculateMappedWorkbook` は既定で true（全数式の計算結果を保存）。
false にすると参照書換えと全数式キャッシュ失効までを行い、後続 Reader に対象評価を委ねます。
描画 CLI の Mapping 統合はこの順序を使います。
`MappingOptions.FormulaEvaluationOptions` と Reader の同名プロパティで上限を設定できます。

OOXML の `cm` / `vm` 計算メタデータも、動的配列・rich value を誤ってスカラーとして扱わないため、到達時は保守的に拒否します。

実行可能な [請求書サンプル](../samples/ExcelRenderer.Formulas.Sample/README.ja.md) は XLSX を生成し、0/1/3/4明細の計算とPDF・PNG・SVG・Markdown出力を検証できます。

## 帳票関数の追加

対応は条件集計・完全一致検索・日付6関数・文字列8関数を含む40関数です。
組込みDataSourceは索引付きの疎範囲を使い、既存の外部DataSourceも継続利用できます。
日付系はブックに従い、UTF-16 v1と既存の文字列資源制限を維持します。
Excel実機比較は未実施です。[正解の出典と一括取得手順](formulas-oracle.md)、
[今回の検証記録](formulas-expansion-validation.md)を参照してください。
