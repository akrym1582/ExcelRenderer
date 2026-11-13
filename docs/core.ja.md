# ExcelRenderer Core

利用ガイド: [本体/Core比較](main-core-parity.ja.md) · [診断と出力](diagnostics.ja.md)


[English](core.md) | 日本語

XLSXからPDFだけを生成する独立したソースプロジェクトです。`netstandard2.1`で、.NET 8/10からProjectReferenceできます。Core単独のNuGetパッケージ、専用CLIパッケージ、フォント入りDLLは作成しません。既存の ExcelRenderer.Tool の `core` サブコマンドから試せます。本体パッケージには同じbuildのCore DLL/XMLを同梱します。

公開型には `ExcelConverter`、`PdfExportOptions`、`ExcelReadOptions`、`ConversionResult`、`ConversionDiagnostic` があります。名前空間は `ExcelRenderer.Core` で、設定や結果のメンバーは以下に示すCore版の対応範囲に合わせています。

```xml
<ItemGroup>
  <ProjectReference Include="../ExcelRenderer/src/ExcelRenderer.Core/ExcelRenderer.Core.csproj" />
</ItemGroup>
```

```csharp
using ExcelRenderer.Core;
using ExcelRenderer.OpenXml.Reader.Input;

using var xlsx = File.OpenRead("report.xlsx");
using var pdf = new FileStream("report.pdf", FileMode.Create, FileAccess.ReadWrite);
var result = await ExcelConverter.ConvertAsync(xlsx, pdf, new PdfExportOptions
{
    FontFilePath = "/fonts/NotoSansJP-Regular.ttf",
    SheetName = null,
    Input = new WorkbookInputOptions { MaxInputBytes = 128 * 1024 * 1024 },
});
Console.WriteLine(result.PageCount);
```

## 入力設定の移行

`PdfExportOptions.Input` は `ExcelRenderer.OpenXml.Reader.Input.WorkbookInputOptions` を直接使用します。旧公開型 `ExcelRenderer.Core.WorkbookInputOptions` とCore内部の入力準備ラッパーは削除しました。破壊的API変更なので、明示的な型名・usingをReader.Inputへ変更し、同じbuildの製品DLLで利用側を再ビルドしてください。互換クラスやバイナリ互換の型別名はありません。`Input = new() { ... }` は引き続き使用できます。

Readerの設定は可変の `sealed class` / `set` のままです。旧入力recordの `with` 式は、新しい `WorkbookInputOptions` に必要な値を指定する形へ変更してください。既存の `PdfExportOptions options` から別の入力設定を作る例です。

```csharp
var separate = options with
{
    Input = new WorkbookInputOptions { MaxInputBytes = 64 * 1024 * 1024 },
};
```

`PdfExportOptions` 自体はrecordですが、`options with { ... }` はInputを深くコピーしません。変換中・並列変換中に共有入力設定を変更せず、別設定が必要な変換ごとに別インスタンスを作ってください。Input自身のinitと既定値の `new()` は維持しています。本体の `ExcelRenderer.Rendering.WorkbookInputOptions` recordとMapping・OpenXml・ToolのAPI契約は維持します。[今回の検証結果](core-input-unification-validation.md)を参照してください。

## フォントと表示

呼出し側が指定する単体の固定 TrueType sfnt、または TTC/OTC 内の選択 face を使います。FontCollectionIndex（0始まり、既定0）で選び、計測・描画前に同じ単体 face へ正規化します。既存の非可変カラー face は共通 Skia カラー経路を使います。拡張子では判定しません。CFF/CFF2・可変・WOFF/WOFF2・旧 true/typ1 は PDF 対象外です。破損・未対応形式は InvalidDataException、不存在は FileNotFoundException、不正 index は引数エラーです。[形式の契約](managed-truetype-subset.ja.md)を参照してください。

描画フォントbytesは変換ごとに一度読み、同じsnapshotで文字幅とPDF描画を処理します。列幅は元ブックのNormalのファミリー・サイズ・太字・斜体を基準に、描画用の代替フォントと分けて計測します。`ReadOptions.MaximumDigitWidthPixels96Dpi`は常に優先します。次に`ReadOptions.SourceFontFile`と任意の`SourceFontCollectionIndex`で指定した列幅計算専用のフォントを使います。指定がなければ、描画フォントの物理ファミリー名（ローカライズ名を含む）がNormalと一致する場合はそのsnapshotを実測し、不一致ならSkiaのシステムフォント検索で元ファミリーを取得します。列幅計測だけの検索で、`UseSystemFontsForColumnWidths = false`（Core CLIでは`--no-system-fonts`）で無効化できます。別ファミリーのシステム代替フォントは元フォントとして扱いません。

元フォントを取得できない場合の`ReadOptions.FallbackMaximumDigitWidthPixels96Dpi`の既定値は、96 DPIで8px固定です。描画用フォントやNormalサイズには比例させません。同プロパティを明示的に`null`にするか、CLIで`--column-width-fallback-metrics font`を指定すると、描画用の代替フォントをNormalのサイズで実測します。`--column-width-fallback-mdw`の数値指定があればそちらを優先します。Normal情報がない場合の基準サイズは11ptです。ファミリー別の固定MDWは使用しません。`MaximumDigitWidthFallback`に代替フォントまたは指定値・使用したMDWを記録します。Excelに対して列幅・折返し・ページ数が変わることがあります。取得できた元フォントの実測や常時優先する幅指定は`MaximumDigitWidthResolved`へ記録し、Core CLIでは`--verbose`で表示します。

8pxの基準は、日本語版Windows Excelの游ゴシック11pt・96 DPI・表示倍率100%での半角数字の最大幅（MDW）です。
表示例「列幅8.38（72px）」から余白・罫線の5pxを引くと、`(72 − 5) ÷ 8.38 ≈ 7.995`となり、8pxと整合します。
すべてのフォント・サイズの実測値を8pxとみなすものではありません。画面上の列幅は丸められており、
保存されたSpreadsheetMLのraw列幅は、この表示値とは分けて換算します。

`UseSourceFontForColumnWidths = false`（CLIの`--column-width-metrics fallback`）では、元フォントの検索と照合を行いません。
描画用フォントがNormalと一致していても元フォントの寸法としては使わず、既定の8px・指定した数値・明示的に選んだ代替フォントの実測値を使います。
Windowsに元フォントがある場合でも、元フォントがない環境での列幅計算を再現できます。
`SourceFontFile`とは併用できません。常時優先するMDWの明示指定は有効です。
`UseSystemFontsForColumnWidths = false`はOS検索だけを止め、一致する供給フォントの実測は行うため、このモードとは異なります。

```sh
# 通常は元フォントを実測し、取得できなければ8px固定。
excelrenderer core report.xlsx --font-file NotoSansJP-Regular.ttf -o report.pdf
# 元フォントを取得できない場合に、描画用の代替フォントを実測。
excelrenderer core report.xlsx --font-file NotoSansJP-Regular.ttf --column-width-fallback-metrics font -o font-metrics.pdf
# 元フォントがインストールされていても、その検索・照合をせず計算。
excelrenderer core report.xlsx --font-file NotoSansJP-Regular.ttf --column-width-metrics fallback -o source-free.pdf
# 元フォントを取得できない場合だけ7pxを使用。
excelrenderer core report.xlsx --font-file NotoSansJP-Regular.ttf --column-width-fallback-mdw 7 -o compatible.pdf
# 列幅計算専用に元フォントを指定。
excelrenderer core report.xlsx --font-file NotoSansJP-Regular.ttf --layout-font original.ttc --layout-font-index 0 -o measured.pdf
```

セルとヘッダー/フッターは通常書体に統一します。太字・斜体の疑似描画、描画用フォントの探索・fallback、IVSの異体字形の再現、外字・カラー絵文字の特別処理はありません。セルとヘッダー/フッターの異体字セレクター（U+FE00–U+FE0F、U+E0100–U+E01EF）は計測・描画前に除去し、基底文字を表示します。例えば「葛」＋IVSは「葛」として表示し、セレクター由来の追加の豆腐や文字幅は生成しません。基底文字自体が指定フォントに未収録の場合の表示は保証しません。通常セル全体のサイズ・色・下線を維持します。リッチテキストの部分書式は未対応です。共有文字列・inline文字列のrunは本文へ連結し、通常セルのフォントで描画します。run個別のサイズ・色・下線・太字・斜体は維持しません。未収録文字は指定フォント/Skiaの欠字表示に従い、すべてのUnicode文字の表示は保証しません。指定した描画フォントによって折返しは変わり得ます。元のNormalフォント寸法を利用できない場合の列幅はExcel原本と異なることがあり、OS検索は列幅計測に限定します。

## 印刷と画像

`SheetName` と `SheetNames` が両方 null なら、非表示状態でも追加フィルターせず、ブック順に全シートを出力します。`SheetNames = new[] { "Sheet3", "Sheet1" }` で複数シートを指定順に出力し、重複を除きます。既存の単一 `SheetName` も利用できます。併用、空リスト、空文字/null の名前、不存在の名前は `ArgumentException` です。空シートは1ページです。Excel内の単一/複数印刷範囲、用紙、余白、倍率、FitToPages、ページ順、繰返し行列、改ページ、結合セル、背景、罫線、配置、折返し、ShrinkToFit、ヘッダー/フッターを維持します。ヘッダー日時は1変換に1つのsnapshotです。セル文字の回転は無視します。

折り返し・縮小表示が無効な通常の横書きセル文字は、同じ行の連続した空白セルへはみ出し、最初の値・数式・結合範囲の手前で切ります。明示的な空文字列、書式で見えない値、空文字列を返す数式、キャッシュのない数式・共有数式も占有扱いです。書式だけの空白セルは通過し、非表示列の幅はゼロです。左揃えは右、右揃えは左、中央揃えは左右を個別に制限し、文字の元の配置は維持します。ページ、明示的な印刷範囲、見出しと本文の境界を越えず、元セルの行高で追加の縦クリップは行いません。結合された元セルや特殊な横揃え・文字方向は既存処理を使います。占有索引と文字クリップは通常版と共有します。

画像は元画像全体を配置枠へ伸縮し、位置・サイズ・Z順を維持します。開始点が所属する印刷領域のページにだけ出力し、はみ出しはPDFページ境界で切れます。開始点が印刷範囲外なら省略します。後続ページへの複製・分割、繰返し行列由来の画像複製、crop・回転・反転を行いません。同じ開始点を含む複数印刷範囲では領域ごとに印刷します。

図形・吹き出し・図形内文字・クリック可能なPDFリンクは生成しません。リンク付きセルの表示文字を保持し、HYPERLINKを含む数式は元XMLの型付き保存結果だけを表示します。OpenXml.Readerが不変の保存値を直接書式化します。保存結果がなければ空表示と診断を返し、friendly textでは補いません。Coreは保存結果専用です。CoreのFormulaDisplayModeとFormulaEvaluationOptionsは削除しました。再計算には本体またはOpenXmlの再計算APIを使ってください。通常セルは通常の書式処理に従います。仕様上無視する図形や画像加工ごとの大量警告は作りません。画像decode失敗などの非致命的な診断はresult.Diagnosticsで取得できます。

APIのセル範囲、ページ抽出、trim、連続画像、PNG/SVG/Markdownはありません。

## Stream・資源契約

入力は現在位置から読み、seekable/nonseekable に対応します。出力は書き込み可能であることが必須です。seekable 出力は従来どおり開始位置 0・長さ 0 が必要です。非 Seek 出力は Position/Length の照会や初期化を行わず受け付けるため、呼び出し側が新しい PDF を書ける出力先を用意してください。最終出力は順番に await する WriteAsync のみを使用し、同期 I/O 禁止の ASP.NET Core Response.Body に直接書けます。Core は出力を Flush しません。必要なら呼び出し側で FlushAsync を行ってください。成功・失敗・キャンセル時とも、呼び出し元の両 Stream を閉じません。

Skia は全ページの中間 PDF を seekable スプールへ生成します。上限付きメモリを使用し、必要なら一時ファイルへ退避します。構造解析後に最終出力が始まり、ページごとの即時送信ではありません。最終 PDF 全体の byte 配列や MemoryStream は追加しません。構文集約は 64 KiB、ペイロード転送は 80 KiB のバッファで、フォント・画像・レイアウト・native リソースには別途メモリが必要です。読み込み・レイアウト・描画は同期処理のままです。キャンセルは OperationCanceledException で、読み込み・ページ境界・await する最終書き込み時に確認します。同期ライブラリ処理を瞬時には中断できません。HTTP 送信後に失敗した場合、送信済みバイトを JSON エラーに差し替えることはできず、ホスト側で処理します。

入力既定値はメモリ16MiB、最大128MiB、ZIP entry 10,000、展開合計512MiB、一時ファイル不許可です。許可したspoolのtempは成功・失敗で削除します。Save時のI/O失敗やキャンセルで途中データが残る場合があり、API全体の原子的書込みは保証しません。[sample](../samples/ExcelRenderer.Core.Sample/Program.cs)は同じディレクトリの一時ファイルへ書き、streamを閉じてから目的ファイルへ移動し、失敗時は一時ファイルを削除します。

```bash
dotnet run --project samples/ExcelRenderer.Core.Sample -c Release -- input.xlsx output.pdf /fonts/font.ttf
```

font収集と描画の各passで1ページのpayloadだけを生成します。Coreが共通Skia文書・subset・
基本描画・incremental finalizerを所有します。文字cacheは512件のCLOCK・入力2048文字以下・保持text/行文字/glyph合計131072単位までです。
収集周は文字のみを構築し、描画位置はnative run bufferへ直接書き込み、rendererごとにPaint最大2個を再利用します。
[性能改善の詳細](core-performance-optimization.md)も参照してください。decoded画像の
推定cache上限は64MiBです。PDF中間spoolは8MiBを超えると一時ファイルを使い、成功・失敗時に
削除します。入力spoolの設定とは独立しています。Skia文書の資源も保持するため定メモリではありません。

フォントは一度の読込によるimmutable snapshotで計測・描画し、ptのadvanceと96dpiの丸めMDWを
区別します。グローバルresolver/reset/gateはありません。main/Coreの混在並列でもnative資源を
変換scopeごとに所有します。Core にも SkiaSharp native assets が必要です。通常 TrueType の subset・TTC 抽出は C# で実装しています。
Pythonや外部PDF readerは製品runtimeの要件ではありません。

限定finalizerは用紙box精度0.001pt、RGB page group、画像補間を維持し、元のcontent/font/画像
stream bytesを保持します。画像辞書の追記revisionは圧縮済み画像bytesを再掲するため、画像の
多い出力は大きくなる場合があります。

## ソースと保守

取り込み元はmain 1.7.2、コミット`32e9165d145516dbd0bcb4c2ae31a78ce6774bce`です。[ファイル対応表](core-source-map.md)に選択ソースの移植元・移植先を記録しています。既存本体・Fonts・Toolへの参照、既存ソースのCompile Include、wrapperはありません。共通処理の修正はCoreへ行い、本体とCoreの方針差を両方の回帰テストで確認します。依存versionは基準版と同じです。テストにコピーするNoto素材は既存OFLと著作権表示を維持し、Core本体には埋め込みません。

## 検証

Linux のライブラリ利用では、アプリ側に SkiaSharp と同じバージョンの native assets を明示追加します。[README のインストール手順](../README.ja.md#linux-でライブラリを使う場合の追加手順)を参照してください。CLI は同梱しています。

[検証結果と再現手順](core-validation.md)を参照してください。Windows CIの結果は、このLinux環境の実行結果とは区別します。

ファイルの対応・所有者・製品ごとの差分は[所有者表](core-source-map.md)を参照してください。

ヘッダー/フッターは既存のフィールド置換・配置を継承します。Excel独自の書式制御コードを新たに解析する処理は追加していません。LinuxのSkia native層は初期化時にfontconfigへアクセスする場合があります。指定フォント以外のfaceは選択しませんが、native層の初期化アクセスが皆無とは主張しません。

## 共通実装

入力準備/spoolはReaderが所有します。画像資源、workbook/cell走査、style intern、幾何・ページ計画/構築、文字placement、セルcommand生成、診断集計、基本PDF描画/SaveはCoreへ集約しています。本体の公開型、完全font painter、画像変形、図形、リンク、PNG/SVG/Markdown、選択/trim、連続出力は本体に残ります。

Full reader/layout/drawing/PDF adapterが高機能部分を拡張し、本体の公開APIは既存assemblyに維持します。[構成](core-architecture.ja.md)、[検証結果](core-refactor-validation.md)と[所有者表](core-source-map.md)を参照してください。

本体/Coreは同じ共通実装を利用し、資源は変換単位で所有します。本体とCoreは同じbuildから配布してください。[撤去の検証記録](pdfsharp-removal-validation.md)も参照してください。

Coreは常にOpenXml.Readerを使い、保存値を描画します。汎用の入力検査・spoolもReaderが所有します。[統合説明](openxml-integration.ja.md)を参照してください。

コレクションは `PdfExportOptions.FontCollectionIndex`（0始まり、既定0）で選択します。[形式の詳細](managed-truetype-subset.ja.md)を参照してください。

## Reader分離後の依存

Coreが参照する自作プロジェクトは `ExcelRenderer.OpenXml.Reader` だけです。既存OpenXml・Formulasへの直接・推移依存はありません。`PdfExportOptions.ReadOptions` の数式モード・評価オプションを指定していた呼び出しは修正して再ビルドしてください。本体とCLIの明示再計算は維持しています。[移行案内](openxml-reader-split.md)を参照してください。

`python3 scripts/validate-reader-isolation.py` はCore／Reader単独consumerを通常のrestore/build/publishで作り、出力DLL・依存グラフ・AssemblyRefを監査し、Core単独で保存済み数式結果を使ったPDFを生成します。

## CLI から Core を試す

既存の ExcelRenderer.Tool パッケージに含まれるサブコマンドです。

```bash
excelrenderer core input.xlsx -o core.pdf --font-file ./regular.ttf --sheet Sheet3 Sheet1 --verbose
```

`--font-file` は必須です。コレクション内の face は `--font-collection-index`（0始まり、既定0）
で指定できます。`--sheet` は複数名と繰り返し指定に対応し、省略すると全シートを選択します。
出力先は新規ファイルに限り、失敗時は一時 PDF を削除します。`--verbose` は実行引数、
所要時間、変換メトリクス、例外詳細を標準エラーに出力します。Core の診断は常に表示します。
`--data data.json` で先に本体の Mapping を実行することもできます。
Core の変換自体は保存済み値を読み取り、数式を再計算しません。


## 小数の列幅を保持するオプション

`PdfExportOptions.ReadOptions.UseLibreOfficeColumnWidths` の既定値は `false` です。
`true` を明示すると、供給した通常書体をブックのNormalサイズで計測し、数字幅をtwip単位
（96dpiで1/15ピクセル）へ丸め、列幅の小数ピクセルを保持します。
`MaximumDigitWidthPixels96Dpi` を明示すれば計測値より優先し、描画フォントから独立した寸法を指定できます。
数値は有限かつ1以上である必要があります。MDWだけの指定では小数計算へ切り替わりません。

```csharp
var options = new ExcelRenderer.Core.PdfExportOptions
{
    FontFilePath = "fonts/NotoSansJP-Regular.ttf",
    ReadOptions = new()
    {
        UseLibreOfficeColumnWidths = true,
        MaximumDigitWidthPixels96Dpi = 8.8,
    },
};
```

```bash
excelrenderer core input.xlsx -o core.pdf --font-file NotoSansJP-Regular.ttf --column-width-metrics libreoffice --column-width-mdw 8.8
```

CLIは `source`（既定）と `libreoffice` に対応します。通常版のレイアウト専用フォントの
ファイル・index指定と `legacy` モードはCoreにはありません。
8.8pxは今回の参照環境のNoto Sans Mono・11pt用の例です。省略時は供給フォントを計測するため、
参照先のフォント代替が違えばセル幅も異なり得ます。LibreOfficeのフォント探索やヘッダー位置を
自動再現する設定ではありません。保存行高と印刷倍率は維持しますが、列幅に依存する折り返しや改ページは変わります。
[通常版・Coreの設定比較](../README.ja.md#libreoffice互換の列幅計算を選ぶ場合)と
[セル実寸の検証結果](validation/cell-width-compatibility/README.md)を参照してください。
