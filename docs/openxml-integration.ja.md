# Open XML エンジンの選択

Reader・Mapping の既定は従来どおり ClosedXML です。
`ExcelRenderer.OpenXml` を pack 可能な共通パッケージとし、Core・本体・Mapping
が参照します。OpenXml 自体は描画・Mapping 言語・フォント・ClosedXML を参照しません。
NuGet 公開は実装・ローカル pack と別の作業です。

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
| ClosedXml（既定） | Legacy / CachedValue | 既存 Reader |
| OpenXml | CachedValue | 新 Reader。診断は出力選択後に集約 |
| OpenXml | Legacy / Recalculate | 出力前に拒否 |
| Auto | Legacy | ClosedXML。理由は `LegacyFormulaMode` |
| Auto | CachedValue | 能力検査後に選択 |
| 全エンジン | Recalculate | 引き続き未対応 |

新 Reader は保存済み数式結果のみを表示します。literal HYPERLINK の表示は
cache 欠落診断の前に復元します。共有式 follower の相対・絶対・混合 A1 参照、
範囲、全行・全列、文字列、シート名、関数を tokenizer で扱います。
構造化参照・外部参照・3D 参照など未知の共有式文法には
`FormulaTextUnavailable` を通知し、Auto は描画対象なら旧 Reader を選びます。
この処理は Mapping の式編集や数式評価を行いません。

Auto の表示能力検査にはシート、明示範囲、hidden を反映します。ページ指定では
通常の layout と source region による preflight を行い、対象外の警告だけで
旧経路へ切り替えません。この場合は候補モデルの構築とフォント gate 内の
レイアウト検査が必要です。旧 Reader を選ぶ場合は候補を解放してからロードします。
この追加処理も Auto のコストに含まれます。入力破損・上限超過・キャンセル・
実行時エラーを捕まえて別エンジンで再試行しません。

地域依存・未解決の表示は同等とみなしません。`UnsupportedNumberFormat`、
`RegionalNumberFormat`、`IsoDateDisplay`、`UnresolvedColor`、
`UnsupportedPrintReference`、`FormulaTextUnavailable` は明示 OpenXml で既存の
strict / warnings-as-errors に従います。組み込み日付形式14は保存された
`mm-dd-yy`、ISO 日付セルは元テキストを表示します。地域ごとの日付表記を固定する場合は
テンプレートに明示した custom number format を使ってください。数式 cache の欠落や
エラーは診断であり、Reader を再試行する理由にはしません。

## 数式なしの Mapping

構文解析・CLR/JSON 評価・評価済み展開計画を両 Writer で共有します。スカラー、
escape、format/date、null、1行配列、入れ子、空配列、書式だけのセル、行列情報、
対応可能な結合、印刷範囲・タイトル、既存改ページ・directive を処理します。
新文字列は inlineStr、未変更の rich/shared string は保持します。`=SUM(...)` で
始まる値も文字列です。DateTime/TimeSpan は元の表示形式を保ち、General の場合だけ
既定の日付・時間形式を追加します。

能力検査は全シートを対象とし、データの getter / IEnumerable を評価しません。
数式、一般定義名、条件付き書式、入力規則、table/pivot/filter/sparkline、
描画・コメント・リンク、外部データ、マクロ・署名・Strict OOXML、未知の
worksheet child・extension・関係 part は初期 Writer の範囲外です。
明示 OpenXml は理由を列挙して出力前に失敗し、Auto は旧経路を選びます。
ClosedXML が保存する空の `tableParts` は許容しますが、実際の table 参照は拒否します。
旧経路でも繰り返し内の数式・定義名・境界を跨ぐ結合・table/画像の既存制約は維持します。

Auto は印刷範囲と展開領域が交差すると `PrintAreaExpansionCompatibility` で旧経路を
選びます。明示 OpenXml の印刷範囲は全出力 occurrence を含むように拡張し、全行削除の
area は除去します。繰り返し・削除に交差する行タイトルは対象外です。固定タイトル、
一意に移動する行タイトル、列タイトルは保持します。

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
`--mapping-backend` を追加しました。値は `closedxml|openxml|auto`。
`--mapping-backend` の明示指定には `--data` が必要です。`--verbose` の選択理由は stderr
へ表示します。OpenXml + Legacy を勝手に CachedValue へ変更しません。

汎用入力検査・spool は OpenXml/Input、描画モデルの共通処理・結合・layout は Core、
製品固有の run・図形・リンクは本体、テンプレート言語と最終行配置は Mapping が所有します。
Writer は型付き編集計画だけを受け取り、spill 可能な元行ストアから別 ZIP stage を生成し、
関係 part・SST count を保って新 Reader で再読込します。schema 検証と未変更 part の hash は
別途テストします。実測・比較結果は接続検証記録を参照してください。先行 Reader 単体の
過去の検証結果を接続済みの証拠にはしません。

文字列中の `_xHHHH_` というリテラルと XML 1.0 で直接書けない制御文字は、
SpreadsheetML の escape 規則で保存し、新 Reader で元の文字列へ復元します。
ClosedXML 0.105.1 は plain inlineStr の escape を復号しないため、これらの特殊値を
同ライブラリだけで再読込すると escape 表記が残ります。通常文字列と先頭アポストロフィ
（quotePrefix）は旧 Mapping と一致させています。
