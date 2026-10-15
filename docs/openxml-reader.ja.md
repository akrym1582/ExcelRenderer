# 独立した Open XML Reader

`ExcelRenderer.OpenXml` は、XLSX から描画用データを読む非配布の .NET Standard 2.1 ライブラリです。実行時の依存は DocumentFormat.OpenXml **3.3.0** と ExcelNumberFormat **1.1.0** のみです。ClosedXML、Core、Mapping、フォント、描画ライブラリは参照しません。既存の変換処理と CLI は従来の Reader を使います。新 Reader は比較検証と将来の接続用であり、ClosedXML の置換完了を意味しません。

## 使用方法と入力の所有権

```csharp
using var input = File.OpenRead("validated.xlsx");
using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions
{
    SelectedSheetNames = new[] { "Report" },
    Culture = CultureInfo.GetCultureInfo("ja-JP"),
    DiagnosticSink = diagnostic => Console.Error.WriteLine(diagnostic.Code),
});

var sheet = reader.Sheets.Single(s => s.Name == "Report");
var info = reader.ReadWorksheetInfo(sheet.Index);
foreach (var cell in reader.EnumerateCells(sheet.Index))
{
    var style = reader.ResolveStyle(cell);
    var display = reader.FormatCell(cell);
    // 逐次消費する。全セルを保存するかは呼び出し側の判断。
}
```

例では `ExcelRenderer.OpenXml`、`System.Globalization`、`System.Linq` を import します。この低レベル API は、上位層で検証済みの、読み取り・seek 可能で Position=0 の入力を要求します。アップロード・ZIP の検証と spool は上位層の責務であり、ここには複製しません。成功・失敗・列挙中断・Reader 破棄後も呼び出し元のストリームは開いたままです。Position の復元は保証しません。

シート index は **元のブック配列内の1始まり位置** で、sheetId や relationship ID とは異なります。`Sheets` には hidden/veryHidden と chart sheet も元の順序で残ります。chart sheet のセル・worksheet メタデータ取得は `NotSupportedException` です。選択名は大文字小文字を区別した Ordinal 一致です。null は全ワークシート、空リストは本文対象なし、重複名は排除、存在しない名前は Open 時に失敗します。選択外でも明示的な `ReadWorksheetInfo` は可能ですが、セル列挙はできません。

同じ Reader で同時に実行できるのはセル列挙またはメタデータ操作1つです。列挙中の別操作は、キャッシュ済みメタデータの取得も含め `InvalidOperationException` になります。途中終了を含め enumerator を破棄してください。通常の `foreach` は自動で処理します。並行処理には別 Reader を使ってください。汎用的な thread-safe サービスではありません。列挙中も `ResolveStyle` と `FormatCell` は利用できます。Reader 破棄後の操作・プロパティ取得は `ObjectDisposedException` です。`CellRecord` と内包データは、列挙の次ステップや Reader の破棄後も使える不変スナップショットですが、書式解決には生存中の Reader が必要です。

## 走査と保持メモリ

Open はブック情報・書式・テーマを読みますが、シート本文と共有文字列は読みません。シートの初回要求ではメタデータを走査し、セル列挙では同じ part を再走査します。メタデータには行定義、正規化した列区間、結合、数式 master、array 数式の行区間索引を保持します。全セル辞書やシート全体の DOM は保持しません。セル列挙では SDK の1セルだけをロードし、独立した不変データに変換します。中断・例外・キャンセル時には part reader を閉じ、再列挙では先頭から読み直します。

共有文字列は最初の `t=s` セルで、entry 単位で読みます。**共有文字列表は全体をメモリに保持します**。小さい選択シートだけでも、共有表を使えば全体のコストが発生します。ディスク索引、プロセス全体の intern/cache はありません。rich string の不変 run 配列は共有されます。run の null 属性は「セルのフォントから継承」を意味します。セルごとに基底フォントを解決してから、指定された run 属性だけを重ねてください。phonetic は表示本文に混ぜず、存在情報と保持コストを扱います。OOXML `_xHHHH_` は1回だけデコードし、`_x005F_x0041_` はリテラル `_x0041_` のままになります。

メモリは共有文字列・書式・行列メタデータ・結合・数式グループに応じて増えます。**定メモリではなく**、次の上限もプロセス RSS の上限ではありません。2回のシート走査とセルごとの DOM/変換コストにより、密な表では ClosedXML より遅い場合があります。[実測記録](openxml-reader-validation.md)を参照してください。

| Option | 既定値 | 意味 |
| --- | ---: | --- |
| `MaxCharactersInPart` | 256 Mi | part ごとの XML 文字数。SDK 3.3.0 が適用 |
| `MaxSharedStringItems` | 1,000,000 | 共有文字列の件数 |
| `MaxSharedStringCharacters` | 32 Mi | phonetic を含む共有文字列の元 UTF-16 本文合計 |
| `MaxStyleRecords` | 100,000 | 書式の各表の件数 |
| `MaxCellTextCharacters` | 1 Mi | 1セルまたは1共有文字列の元 UTF-16 本文。数式・phonetic も含む |

上限は正数で指定します。超過は `InvalidDataException` となり、切り捨てや別 Reader への fallback は行いません。XML 構文・part 上限エラーは、元の例外を inner exception に残して `InvalidDataException` に統一します。キャンセルは走査ループと各 entry で確認します。SDK が1要素を同期ロードしている途中の即時停止は保証しません。Culture は Open 時の read-only snapshot、数値書式 cache は Reader ごとに最大8,192コードです。

## データと元の単位

XML に存在する全 `c` を返します。書式だけの空セル、保存済み空文字、結合内のセルも保持します。欠落セルや結合の先頭セルを補完しません。`RawDimension` はヒントにとどめ、実範囲は実際の座標から計算します。省略された行・セル座標は XML 順に推定します。不整合・重複・逆順・範囲外は失敗します。`CellAddress.Parse` は通常座標、`CellRange.Parse` は `$` 付き絶対 A1 参照も受け付けます。

`CellValue.Kind` は `Blank`、`String`、`Number`、`Boolean`、`Error`、`IsoDate` です。数値は有限の double と `CellRecord.RawValue` を別に保持します。boolean は0/1のみで、false は `FALSE` と表示します。ISO 日付は検証後もタイムゾーン表記を含め元の文字列を保持し、UTC/ローカルに変換しません。不正数値・共有文字列 index は空セル化せず失敗します。

| メタデータ | 単位・表現 |
| --- | --- |
| `RowInfo.HeightPt`、`DefaultRowHeightPt` | pt。既定行高の省略時は Reader の既定15 |
| `ColumnInfo.Width`、`DefaultColumnWidthExcel` | 保存された Excel 列幅。ClosedXML 補正やフォント計測なし |
| `BaseColumnWidthCharacters` | 文字数。OOXML 既定8 |
| `PageMargins` の属性 | inch |
| `PageSetup` / `PageSetupProperties` | 元の ID/属性。scale と fitToPage を分離し、fit=0 も保持 |
| `HeaderFooter` | 原文。制御コードは展開しない |
| `RowBreaks` / `ColumnBreaks` | 各 id/min/max/man。count 属性を件数として信用しない |
| `ElementInfo` | SDK オブジェクトを含まない不変 name/attributes/children/leaf text |

行は customHeight、hidden、customFormat/style、outline、collapsed を保持します。列は区間のまま、後出しの定義を優先します。`SheetFormat` の元属性では、省略と明示0、`zeroHeight` を区別できます。重複結合は排除し、部分的に重なる結合は失敗します。印刷範囲 union の順序は保持し、外接矩形に統合しません。

印刷定義には原文と元の0始まり `localSheetId` を残します。同じシートの A1 矩形・全行・全列を解釈でき、引用符内のカンマや二重アポストロフィも扱います。外部ブック・3D参照・関数/OFFSET・他の定義名を介する参照は `UnsupportedPrintReference` とし、**その定義全体**の resolved ranges を空にします。

## 書式と表示文字列

書式の優先順は、明示 cell `s`（0を含む）、`customFormat=true` の row `s`、column style、cell-XF 0 です。`apply*=false` だけを理由に保存成分を破棄しません。欠落成分は `xfId`、次に既定値へ fallback します。apply 属性と quotePrefix は `CellStyleInfo.Attributes` に残します。Normal フォントは表示名ではなく builtinId=0 と style-XF から選びます。styles.xml がない場合は暗黙の General を許容し、元のフォント情報がない部分は将来の adapter で解決します。

フォント・塗り・上下左右/斜め罫線・配置・rotation 255・wrap・shrink・indent の情報を保持します。テーマの major/minor、Latin/EastAsia/script 別フォントは `Workbook.Theme` で取得できますが、フォント fallback エンジンではありません。色は RGB/indexed/theme/auto の元属性を残します。解決可能なら theme/indexed と HLS 輝度 tint による `resolvedRgb` を追加します。未解決色は黒に置換せず、元情報と診断を返します。

数値と文字列用 section は ExcelNumberFormat 1.1.0 で整形します。custom code を優先し、組み込み ID **0–22、37–49** は明示した定義表を使用します。明示 code のない地域依存 ID（23–36、50–81 等）、未検証の locale 指定（`[$…]`）、DBNum、和暦書式は `UnsupportedNumberFormat` と General 表示になります。依存ライブラリで表示可能な書式の一部も保守的に未対応としています。不正な code も診断付き General です。列幅依存の `###`、繰り返し・スペース配置、書式色の描画接続は行いません。

数値シリアルは原値のまま、`isDate1904` 引数で日付系を指定します。serial 60 を `DateTime` に押し込みません。固定ライブラリは、27.5時間に最も近い double から `27:29:60` を返す場合があります。既知差分として記録し、文字列置換では補正しません。

## 数式キャッシュ・診断・対象外機能

保存済み結果だけを読みます。`FormulaInfo.HasCachedResult` は、欠落と保存済み0・false・空文字を区別します。結果がなければ空表示と `FormulaCachedValueMissing`、Excel エラーなら原文表示と `FormulaResultError` です。数式・外部リンクは評価しません。shared follower は自分の原文（空の場合あり）、si、master の address/text/ref を保持し、master が後に出現しても関連付けます。相対参照は書き換えません。array follower は、行区間索引で XML に実在するセルだけを関連付けます。dynamic/dataTable の元情報は残しますが、高度な spill 追跡は対象外です。

診断コードは `ReaderDiagnosticCodes` で定義しています。Code、Severity、任意の元 sheet/address、セル内容や URI を含まない説明を返します。`DiagnosticSink` には通知しますが、Reader 内に全件蓄積しません。`FormatCell` はその呼び出しの少数の診断も返します。書式色の未解決診断は、書式の初回解決時に通知し、セルごとの表示で再通知しません。

`HasDrawings`、`HasHyperlinks`、`HasConditionalFormatting`、`HasTables` は存在情報のみです。XML の検出は描画対応を意味しません。画像バイナリ、DrawingML 図形、ハイパーリンク解釈、条件付き書式評価、編集・行挿入・保存、汎用数式評価、PDF/PNG/SVG/Markdown 接続は対象外です。特に cache のない HYPERLINK 数式は空表示です。

## 次工程の責務と接続

| 既存の所有者 | 新 Reader の対応部分 | 次工程 |
| --- | --- | --- |
| Core `CoreExcelReader`、`FormulaCacheReader` | `OpenXmlWorkbookReader`、`CellRecord`、`FormulaInfo` | 描画モデルへの逐次 adapter を作成後、重複 XML 読取を委譲/削除 |
| Core `WorkbookLayoutMetadataReader` | `WorksheetInfo`、印刷参照 | 元単位・ページ情報の adapter を作り、移管後に全シート DOM 経路を削除 |
| Core `ReaderSession` | 書式テーブル、`FormatCell` | 既存方針への診断・メタデータ変換 |
| 公開/Core 書式 converter | `CellStyleInfo`、rich runs | 描画書式への変換とセル基底フォントごとの run 合成 |
| Core 列幅・フォント計測 | 代替なし | Core で保存 Excel 列幅を計測して pt へ変換 |
| Full reader 拡張点 | 代替なし | ClosedXML 依存の引数を別工程で再設計 |
| Core input preparer、Mapping | 代替なし | 入力検証・spool と編集・保存の責務を維持 |

既存の公開 `ExcelStyleConverter.Convert(IXLCell)` は維持します。既存製品プロジェクトは新 Reader を参照せず、既存 NuGet パッケージにも含めません。実行コマンドと比較・測定方法は[検証ツール README](../tools/ExcelRenderer.OpenXml.Validation/README.md)を参照してください。
