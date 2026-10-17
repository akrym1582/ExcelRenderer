# Open XML エンジンの選択

NuGet 配布は本体・Mapping・Tool・Fonts の4つです。Formulas と OpenXml は
本体と Mapping に同梱される DLL で、個別パッケージは公開しません。
[配布構成](package-distribution.md)を参照してください。

> 明示再計算モードを追加しました。[数式計算ガイド](formulas.ja.md)が評価範囲・キャッシュ失効・対応機能の現在の仕様です。[対応範囲・40関数](formulas-reference.ja.md)も参照してください。以下の保存値／数式拒否の説明は、特記のない限り既定の CachedValue モードに適用します。

[English](openxml-integration.md) | 日本語

Core・本体の Reader は OpenXml が既定です。Auto は OpenXml の互換指定として受け付け、
別エンジンへ戻りません。Reader の明示 ClosedXml は入力処理前に移行先を示して拒否します。
enum の数値は変更しません。Mapping も OpenXml が既定で、Auto は同じ Writer を使います。
Mapping の明示 ClosedXml も入力処理前に拒否します。
[撤去の互換性](closedxml-removal-compatibility.md)を参照してください。
`ExcelRenderer.OpenXml` は共通の Reader・Writer・入力処理を提供し、描画・Mapping 言語・
フォント・ClosedXML を参照しません。

```csharp
var document = new ExcelRenderer.Excel.ExcelReader().Read(input,
    new ExcelRenderer.Excel.ExcelReadOptions
    {
        Backend = ExcelRenderer.Excel.ReaderBackend.OpenXml,
        FormulaDisplayMode = ExcelRenderer.Excel.FormulaDisplayMode.CachedValue,
        BackendSelected = selection => Observe(selection.SelectedBackend, selection.ReasonCodes),
    });

ExcelTemplateMapper.Map(template, output, data, new MappingOptions
{
    Backend = MappingBackend.Auto,
    Input = new() { AllowTemporaryFiles = true },
    BackendSelected = selection => Observe(selection.SelectedBackend, selection.ReasonCodes),
});
```

Core は `PdfExportOptions.ReadOptions` に同等の
`ExcelRenderer.Core.ExcelReadOptions` を持ちます。本体の各変換 API は既存の
`ReadOptions` から選択を伝播します。

| Reader 指定 | 数式指定 | 動作 |
| --- | --- | --- |
| OpenXml（既定） | CachedValue（既定） | ストリーミング Reader。診断は出力選択後に集約 |
| Auto | CachedValue | OpenXml の互換指定。fallback なし |
| ClosedXml | 全指定 | 入出力前に NotSupportedException。OpenXml または Auto へ移行 |
| OpenXml / Auto | Recalculate | 限定的な自作計算器。数式ガイド参照 |
| 全エンジン | Legacy | 入力処理・出力開始前に拒否 |

CachedValue モードの Reader は元 XLSX の保存結果だけを表示します。古い結果もそのまま使い、
鮮度や依存関係は検査しません。欠落は空表示と `FormulaCachedValueMissing`、
Excel エラーはその文字列と `FormulaResultError` です。0・false・保存された空文字は
欠落と区別します。HYPERLINK の定数から静的なリンク先を抽出することはありますが、
friendly text で保存結果や空表示を上書きしません。

式文字列は表示結果とは別のメタデータです。`ReportCell.Formula` は元の本文が
ある場合だけ先頭に `=` を付けて保持します。本文のない共有式・配列式 follower は null、
`FormulaSharedIndex` / `FormulaMasterAddress` に共有 index と master 位置を保持します。
Markdown の `VisualCell` にも伝播します。描画用の式展開はせず、式文字列が展開できない
ことだけで Auto を切り替えません。低レベル公開 `FormulaTextResolver` は既存利用者向けに
残しますが、描画経路では利用しません。

Reader の Auto も OpenXml モデルを一度だけ構築します。診断にはシート・明示範囲・hidden・
ページ指定を従来どおり反映します。互換性走査、候補の破棄、別実装による再読込はありません。
成功時の BackendSelected は一度だけ呼び、SelectedBackend は OpenXml、理由コードは
ExplicitOpenXml または AutoOpenXmlOnly です。無効な指定や入力破損では成功通知しません。
未知の enum 値は ArgumentOutOfRangeException です。入力破損・上限超過・キャンセル・
実行時エラーを別実装で再試行しません。

表示 culture は読込開始時の CurrentCulture を低レベル Reader へ明示して渡し、
セッション用のコピーを保持します。

地域依存・未解決の表示は同等とみなしません。`UnsupportedNumberFormat`、
`RegionalNumberFormat`、`IsoDateDisplay`、`UnresolvedColor`、
`UnsupportedPrintReference` は OpenXml／Auto ともに既存の
strict / warnings-as-errors に従います。組み込み日付形式14は保存された
`mm-dd-yy`、ISO 日付セルは元テキストを表示します。地域ごとの日付表記を固定する場合は
テンプレートに明示した custom number format を使ってください。数式 cache の欠落や
エラーは診断であり、Reader を再試行する理由にはしません。

対応する paperSize の数値 ID とポイント寸法は B4/B5 を含め維持し、landscape では縦横を交換します。
未対応 ID は UnsupportedPaperSize と A4 寸法を使用します。paperWidth／paperHeight の単位付き長さは
旧 Reader と同様に解釈せず、UnsupportedPaperDimensions を通知し paperSize に従います。
これらをポイント値として扱うことはありません。

公開 `ExcelStyleConverter.Convert(IXLCell)` は削除し、
`Convert(CellStyleInfo style, CellValueType valueType)` に置き換えました。破壊的 API 変更です。
null 検証、General 配置、数式キャッシュの値型は[移行例](closedxml-removal-compatibility.md#public-style-api-migration)を参照してください。

## 数式なしの Mapping

既存の構文解析・CLR/JSON 評価・評価済み展開計画を維持します。スカラー、
escape、format/date、null、1行配列、入れ子、空配列、書式だけのセル、行列情報、
対応可能な結合、印刷範囲・タイトル、既存改ページ・directive を処理します。
新文字列は inlineStr、未変更の rich/shared string は保持します。`=SUM(...)` で
始まる値も文字列です。DateTime/TimeSpan は元の表示形式を保ち、General の場合だけ
既定の日付・時間形式を追加します。

全 Mapping バックエンドで、選択通知・getter・IEnumerable 列挙・展開計画の前に、
ブック全体のセル数式（本文のない共有式、配列式、data table を含む）、table の
calculated/totals 式・集計関数、計算を伴う定義名を拒否します。非表示・非対象シート、空配列で
削除予定の行も対象です。`MappingException.ReasonCode` は
`MappingFormulaNotSupported`、可能な範囲で元シート・セル・定義名・table 名を示します。
未知の workbook・worksheet・table extension は、値の数式がないと確認できないため
保守的に拒否します。

明細金額・税・合計は呼び出し元で計算し、`**` の値プレースホルダーへ
渡してください。静的定義名は文字列・数値・真偽値の定数、既知シートの有限 A1 参照の
union（全行・全列・引用シート名を含む）だけを許容します。関数・演算・動的参照・外部参照・
3D 参照・名前の別名・未知文法は拒否します。繰り返し領域と重なる定義名の既存制限は維持し、
静的な印刷範囲・印刷タイトルは継続します。条件付き書式・入力規則の式は構造メタデータとして
対応する A1 参照を移動します。文字列リテラル・関数名は変更せず、式評価や条件による描画も追加しません。

構造上の非対応は getter・列挙前に検査します。元行から出力先への対応に従って、静的定義名、
通常リンクの ref・内部 location、コメントと VML 表示アンカー、繰り返し外の画像、
数式なし table/filter、条件付き書式・入力規則を更新します。別シートからの参照も対象です。
画像バイト列と relationship 先は保持します。通常の worksheet を参照するピボットは参照と出力位置を
移動し、元データが変わる場合は source records と shared items を更新して refresh を指定します。
ピボット集計セルの再計算は行いません。グループ化・計算・外部・解決できない名前付きピボット元、
chart cache、threaded comment、マクロ・署名・Strict・未知の拡張や part は拒否します。
スパークライン拡張は既存の共通数式検査による拒否を維持します。fallback や再試行はありません。
既定モードは元の `calcPr`（不存在も含む）を保持します。明示再計算では数式ガイドに従ってキャッシュと計算フラグを更新します。

印刷範囲は全出力 occurrence を含み、完全に削除された area は除去します。
繰り返し・削除に交差する行タイトルは対象外です。固定タイトル、一意に移動する行タイトル、
列タイトルは保持します。

出力に残る `**@page-break` は A1 でも倍率指定を有効にします。正の元 scale を保持し、
なければ100です。空配列で消えた directive は倍率に影響しません。元行改ページは各出力
occurrence の直後へ、列改ページはそのまま移します。

`MappingOptions.Input` の既定はメモリ16 MiB、入力128 MiB、ZIP 10,000件、展開後512 MiB。
一時ファイルは既定で無効です。大きい row store / stage では許可してください。
呼び出し元の stream は閉じません。file API は入力と同じ path を拒否し、出力と同じ
directory の一時ファイルを完成させて置換します。データ評価・能力検査・stage の再読込が
成功するまで出力 stream に書きません。最終コピー中の I/O 障害では途中書込みがあり得ます。
完全な rollback は保証しません。一時資源は失敗・キャンセル時にも解放します。

## CLI

```console
excelrenderer pdf input.xlsx -o result.pdf --reader-backend openxml --formula-mode cached
excelrenderer xlsx template.xlsx --data data.json -o result.xlsx --mapping-backend auto --verbose
excelrenderer render template.xlsx --data data.json --format svg -o output --mapping-backend openxml --reader-backend openxml --formula-mode cached
```

pdf / image / svg / markdown・md / render に `--reader-backend`、これらと xlsx に
`--mapping-backend` を指定できます。どちらも値は `openxml|auto`、既定は `openxml` です。
明示 `closedxml` は入力・データの読込や出力作成前に移行先を示して拒否します。
`--mapping-backend` の明示指定には `--data` が必要です。`--verbose` の選択理由は stderr
へ表示します。`--formula-mode` は `cached`（既定値）と `recalculate` に対応します。
明示 `legacy` は全 backend で拒否します。`recalculate` は限定的な自作計算器を使います。

## 所有権と検証

汎用入力検査・spool は OpenXml/Input、描画モデルの共通処理・結合・layout は Core、
製品固有の run・図形・リンクは本体、テンプレート言語と最終行配置は Mapping が所有します。
Writer は型付き編集計画だけを受け取り、spill 可能な元行ストアから別 ZIP stage を生成し、
関係 part・SST count を保って新 Reader で再読込します。元行の XML 原型はシートごとの LRU
で最大64行、元 UTF-8 合計256 KiB、1行64 KiBまで保持します。これは元 XML サイズの
制限であり、正確な heap サイズではありません。1回だけ使う行と大きい行は保持せず、各出力行を deep copy
して編集するため、値や座標が次の occurrence へ漏れません。schema 検証と未変更 part の hash は
別途テストします。コマンド、割当の実測値、対象コミットごとの結果は
[検証記録](openxml-integration-validation.md)を参照してください。

文字列中の `_xHHHH_` というリテラルと XML 1.0 で直接書けない制御文字は、
SpreadsheetML の escape 規則で保存し、新 Reader で元の文字列へ復元します。
ClosedXML 0.105.1 は plain inlineStr の escape を復号しないため、これらの特殊値を
同ライブラリだけで再読込すると escape 表記が残ります。通常文字列と先頭アポストロフィ
（quotePrefix）は従来の値の意味を保持します。
