# ExcelRenderer Slim

XLSXからPDFだけを生成する独立したソースプロジェクトです。`netstandard2.1`で、.NET 8/10からProjectReferenceできます。NuGetパッケージ、CLI、フォント入りDLLは作成しません。

公開型名はオリジナル版と同じ `ExcelConverter`、`PdfExportOptions`、`WorkbookInputOptions`、`ConversionResult`、`ConversionDiagnostic` です。名前空間は `ExcelRenderer.Slim` で、設定や結果のメンバーはSlim版の対応範囲に合わせています。従来の `Slim` 接頭辞付きの型名を使用している呼び出し側は、これらの名前へ変更してください。

```xml
<ItemGroup>
  <ProjectReference Include="../ExcelRenderer/src/ExcelRenderer.Slim/ExcelRenderer.Slim.csproj" />
</ItemGroup>
```

```csharp
using ExcelRenderer.Slim;

using var xlsx = File.OpenRead("report.xlsx");
using var pdf = new FileStream("report.pdf", FileMode.Create, FileAccess.ReadWrite);
var result = await ExcelConverter.ConvertAsync(xlsx, pdf, new PdfExportOptions
{
    FontFilePath = "/fonts/NotoSansJP-Regular.ttf",
    SheetName = null,
});
Console.WriteLine(result.PageCount);
```

## フォントと表示

呼出し側が指定する静的TrueTypeアウトラインの単一sfntを使います。拡張子では判定しません。テーブル範囲・必須テーブル・可変/CFF形式を検証し、SkiaSharpとPDFsharpで読込みを確認してからページを生成します。TTC/OTC、CFF OTF、可変フォント、WOFF/TTEは対象外です。破損・未対応形式は`InvalidDataException`、不存在は`FileNotFoundException`です。

フォントbytesは変換ごとに一度読み、同じsnapshotで列幅・行高・文字幅・PDF描画を処理します。セルとヘッダー/フッターは通常書体に統一します。太字・斜体の疑似描画、フォント探索・fallback、IVSの異体字形の再現、外字・カラー絵文字の特別処理はありません。セルとヘッダー/フッターの異体字セレクター（U+FE00–U+FE0F、U+E0100–U+E01EF）は計測・描画前に除去し、基底文字を表示します。例えば「葛」＋IVSは「葛」として表示し、セレクター由来の追加の豆腐や文字幅は生成しません。基底文字自体が指定フォントに未収録の場合の表示は保証しません。サイズ・色・下線を維持します。未収録文字は指定フォント/PDFsharpの通常動作に従い、すべてのUnicode文字の表示は保証しません。フォント置換によりExcel原本と列幅や折返しが変わることがあります。

## 印刷と画像

nullのSheetNameは非表示状態でも追加フィルターせず、ブック順に全シートを出力します。単一SheetNameを指定でき、不存在は`ArgumentException`です。空シートは1ページです。Excel内の単一/複数印刷範囲、用紙、余白、倍率、FitToPages、ページ順、繰返し行列、改ページ、結合セル、背景、罫線、配置、折返し、ShrinkToFit、ヘッダー/フッターを維持します。ヘッダー日時は1変換に1つのsnapshotです。セル文字の回転は無視します。

画像は元画像全体を配置枠へ伸縮し、位置・サイズ・Z順を維持します。開始点が所属する印刷領域のページにだけ出力し、はみ出しはPDFページ境界で切れます。開始点が印刷範囲外なら省略します。後続ページへの複製・分割、繰返し行列由来の画像複製、crop・回転・反転を行いません。同じ開始点を含む複数印刷範囲では領域ごとに印刷します。

図形・吹き出し・図形内文字・クリック可能なPDFリンクは生成しません。リンク付きセルの表示文字とHYPERLINK式のCachedValueは保持します。一般セルは既存と同じGetFormattedString方針で、数式エンジンは追加せず、Excel再計算との同一結果は保証しません。仕様上無視する図形や画像加工ごとの大量警告は作りません。画像decode失敗などの非致命的な診断はresult.Diagnosticsで取得できます。

APIのセル範囲、ページ抽出、trim、連続画像、PNG/SVG/Markdownはありません。

## Stream・資源契約

入力は現在位置から読み、seekable/nonseekableに対応します。出力は書込み可能・seekable・開始位置0・長さ0が必須です。呼出し元の両streamを閉じません。非seekable出力向けの全PDFbufferはありません。キャンセルはOperationCanceledExceptionです。読込み・ページ境界・Saveの書込み境界で確認します。同期ライブラリ処理を瞬時には中断できません。

入力既定値はメモリ16MiB、最大128MiB、ZIP entry 10,000、展開合計512MiB、一時ファイル不許可です。許可したspoolのtempは成功・失敗で削除します。Save時のI/O失敗やキャンセルで途中データが残る場合があり、API全体の原子的書込みは保証しません。[sample](../samples/ExcelRenderer.Slim.Sample/Program.cs)は同じディレクトリの一時ファイルへ書き、streamを閉じてから目的ファイルへ移動し、失敗時は一時ファイルを削除します。

```bash
dotnet run --project samples/ExcelRenderer.Slim.Sample -c Release -- input.xlsx output.pdf /fonts/font.ttf
```

1ページのpayloadだけを生成して最終PdfDocumentへAppendPageし、Saveは1回です。文字layout cacheは512件・文字列2048まで、画像cacheの推定decoded資源上限は64MiBです。画像leaseとnative資源は変換内で解放します。ただしClosedXMLモデルと最終PDF文書は保持され、PDFsharpが文書に保持する画像はcache上限とは別です。定メモリやRSS削減率は保証しません。SkiaSharp/native DLLは画像decode・行高・列幅の計測に必要です。

Slim内のSemaphoreSlimでPDFsharpフォントresetから計測・描画・Save・文書解放まで直列化します。待機はキャンセル可能です。異なるフォントのSlim呼出しは連続・並行で利用できます。同一processで通常版や他ライブラリがPDFsharpのグローバルフォント設定を操作する併用は初版では対象外です。比較は別processで実施します。

## ソースと保守

取り込み元はmain 1.7.2、コミット`32e9165d145516dbd0bcb4c2ae31a78ce6774bce`です。[ファイル対応表](slim-source-map.md)に選択ソースの移植元・移植先を記録しています。既存本体・Fonts・Toolへの参照、既存ソースのCompile Include、wrapperはありません。共通の修正が生じたときは本体とSlimの双方を確認し、片側だけの修正を放置しない保守方針です。依存versionは基準版と同じです。テストにコピーするNoto素材は既存OFLと著作権表示を維持し、Slim本体には埋め込みません。

## 検証

[検証結果と再現手順](slim-validation.md)を参照してください。Windows CIの結果は、このLinux環境の実行結果とは区別します。

<details>
<summary>ソース取り込みのファイル対応</summary>

| 取り込み元 src/ExcelRenderer | Slim内の配置 |
| --- | --- |
| Abstractions/IReportLayoutPass.cs | Abstractions/IReportLayoutPass.cs |
| Abstractions/ITextLayoutService.cs | Abstractions/ITextLayoutService.cs |
| Abstractions/ITextMeasurer.cs | Abstractions/ITextMeasurer.cs |
| Abstractions/TextLayoutLine.cs | Abstractions/TextLayoutLine.cs |
| Abstractions/TextLayoutResult.cs | Abstractions/TextLayoutResult.cs |
| Abstractions/TextSize.cs | Abstractions/TextSize.cs |
| Compatibility/IsExternalInit.cs | Compatibility/IsExternalInit.cs |
| Drawing/BorderStrokeGeometry.cs | Drawing/BorderStrokeGeometry.cs |
| Drawing/DrawBorderCommand.cs | Drawing/DrawBorderCommand.cs |
| Drawing/DrawCommand.cs | Drawing/DrawCommand.cs |
| Drawing/DrawCommandGeneratorPass.cs | Drawing/DrawCommandGeneratorPass.cs |
| Drawing/DrawImageCommand.cs | Drawing/DrawImageCommand.cs |
| Drawing/DrawLineCommand.cs | Drawing/DrawLineCommand.cs |
| Drawing/DrawTextCommand.cs | Drawing/DrawTextCommand.cs |
| Drawing/FillRectangleCommand.cs | Drawing/FillRectangleCommand.cs |
| Drawing/PositionedTextLine.cs | Drawing/PositionedTextLine.cs |
| Drawing/TextLayoutFontSize.cs | Drawing/TextLayoutFontSize.cs |
| Drawing/TextLayoutPlacement.cs | Drawing/TextLayoutPlacement.cs |
| Excel/CellRangeIndex.cs | Excel/CellRangeIndex.cs |
| Excel/ColumnWidthCalculator.cs | Excel/ColumnWidthCalculator.cs |
| Excel/DrawingMLReader.cs | Excel/DrawingMLReader.cs |
| Excel/DrawingPictureMetadata.cs | Excel/DrawingPictureMetadata.cs |
| Excel/ExcelReader.cs | Excel/ExcelReader.cs |
| Excel/ExcelStyleConverter.cs | Excel/ExcelStyleConverter.cs |
| Excel/NormalFontMetadata.cs | Excel/NormalFontMetadata.cs |
| Excel/RawColumnDefinition.cs | Excel/RawColumnDefinition.cs |
| Excel/RawRowDefinition.cs | Excel/RawRowDefinition.cs |
| Excel/SheetPageSetupMetadata.cs | Excel/SheetPageSetupMetadata.cs |
| Slim専用の新規実装 | Excel/SingleFontGraphicEngine.cs |
| Excel/StylePool.cs | Excel/StylePool.cs |
| Excel/WorkbookLayoutMetadataReader.cs | Excel/WorkbookLayoutMetadataReader.cs |
| Slim専用の新規実装 | Fonts/SingleFontContext.cs |
| Slim専用の新規実装 | Fonts/SingleFontResolver.cs |
| GlobalUsings.cs | GlobalUsings.cs |
| Rendering/BufferReadStream.cs | Input/BufferReadStream.cs |
| Rendering/CancellationReadStream.cs | Input/CancellationReadStream.cs |
| Rendering/RenderBufferOptions.cs | Input/InputBufferOptions.cs |
| Rendering/PreparedWorkbook.cs | Input/PreparedWorkbook.cs |
| Rendering/SpillableBufferStream.cs | Input/SpillableBufferStream.cs |
| Rendering/WorkbookInputPreparer.cs | Input/WorkbookInputPreparer.cs |
| Layout/BandIndex.cs | Layout/BandIndex.cs |
| Layout/CellBoundsPass.cs | Layout/CellBoundsPass.cs |
| Layout/CellContentBounds.cs | Layout/CellContentBounds.cs |
| Layout/CellLayout.cs | Layout/CellLayout.cs |
| Layout/ColumnLayout.cs | Layout/ColumnLayout.cs |
| Layout/ColumnLayoutPass.cs | Layout/ColumnLayoutPass.cs |
| Layout/DrawingAnchorResolver.cs | Layout/DrawingAnchorResolver.cs |
| Layout/HeaderFooterLayout.cs | Layout/HeaderFooterLayout.cs |
| Layout/HiddenRowColumnPass.cs | Layout/HiddenRowColumnPass.cs |
| Layout/NormalizePass.cs | Layout/NormalizePass.cs |
| Layout/ObjectGeometry.cs | Layout/ObjectGeometry.cs |
| Layout/PageBand.cs | Layout/PageBand.cs |
| Layout/PageBandBuilder.cs | Layout/PageBandBuilder.cs |
| Layout/PageCellSelection.cs | Layout/PageCellSelection.cs |
| Layout/PagePlacement.cs | Layout/PagePlacement.cs |
| Layout/PaginationPagePlan.cs | Layout/PaginationPagePlan.cs |
| Layout/PaginationPass.cs | Layout/PaginationPass.cs |
| Layout/PrintScaleResolver.cs | Layout/PrintScaleResolver.cs |
| Layout/RectangleGeometry.cs | Layout/RectangleGeometry.cs |
| Layout/RenderBorder.cs | Layout/RenderBorder.cs |
| Layout/RenderCell.cs | Layout/RenderCell.cs |
| Layout/RenderImage.cs | Layout/RenderImage.cs |
| Layout/RenderPage.cs | Layout/RenderPage.cs |
| Layout/RenderPageBuilder.cs | Layout/RenderPageBuilder.cs |
| Layout/RenderText.cs | Layout/RenderText.cs |
| Layout/ReportLayoutContext.cs | Layout/ReportLayoutContext.cs |
| Layout/ReportLayoutEngine.cs | Layout/ReportLayoutEngine.cs |
| Layout/ReportRect.cs | Layout/ReportRect.cs |
| Layout/ResolvePrintAreaPass.cs | Layout/ResolvePrintAreaPass.cs |
| Layout/RowLayout.cs | Layout/RowLayout.cs |
| Layout/RowLayoutPass.cs | Layout/RowLayoutPass.cs |
| Layout/SheetGeometry.cs | Layout/SheetGeometry.cs |
| Layout/SheetLayoutPlan.cs | Layout/SheetLayoutPlan.cs |
| Layout/SheetObjectLayoutIndex.cs | Layout/SheetObjectLayoutIndex.cs |
| Layout/TextLayoutTransform.cs | Layout/TextLayoutTransform.cs |
| Layout/TextMeasurePass.cs | Layout/TextMeasurePass.cs |
| Model/BorderLineStyle.cs | Model/BorderLineStyle.cs |
| Model/BorderSide.cs | Model/BorderSide.cs |
| Model/BorderStyle.cs | Model/BorderStyle.cs |
| Model/CellAddress.cs | Model/CellAddress.cs |
| Model/CellBorder.cs | Model/CellBorder.cs |
| Model/CellRange.cs | Model/CellRange.cs |
| Model/CellStyle.cs | Model/CellStyle.cs |
| Model/ColumnDefinition.cs | Model/ColumnDefinition.cs |
| Model/DrawingAnchor.cs | Model/DrawingAnchor.cs |
| 新規（基底文字への正規化） | Model/DisplayText.cs |
| Model/FontStyle.cs | Model/FontStyle.cs |
| Model/HeaderFooter.cs | Model/HeaderFooter.cs |
| Model/HeaderFooterSection.cs | Model/HeaderFooterSection.cs |
| Model/HorizontalAlignment.cs | Model/HorizontalAlignment.cs |
| Model/IndexRange.cs | Model/IndexRange.cs |
| Model/PageSettings.cs | Model/PageSettings.cs |
| Model/PrintPageOrder.cs | Model/PrintPageOrder.cs |
| Model/PrintScaleMode.cs | Model/PrintScaleMode.cs |
| Model/ReportCell.cs | Model/ReportCell.cs |
| Model/ReportColor.cs | Model/ReportColor.cs |
| Model/ReportDocument.cs | Model/ReportDocument.cs |
| Model/ReportImage.cs | Model/ReportImage.cs |
| Model/ReportSheet.cs | Model/ReportSheet.cs |
| Model/RowDefinition.cs | Model/RowDefinition.cs |
| Model/VerticalAlignment.cs | Model/VerticalAlignment.cs |
| PdfSharp/PdfSharpFinalizedTextPainter.cs | Pdf/PdfSharpFinalizedTextPainter.cs |
| PdfSharp/PdfSharpRenderer.cs | Pdf/PdfSharpRenderer.cs |
| PdfSharp/PdfSharpTextMeasurer.cs | Pdf/PdfSharpTextMeasurer.cs |
| Slim専用の新規実装 | Properties/AssemblyInfo.cs |
| Slim専用の新規実装 | Rendering/CancellationWriteStream.cs |
| Rendering/ConversionDiagnostic.cs | Rendering/ConversionDiagnostic.cs |
| Rendering/ConversionMetrics.cs | Rendering/ConversionMetrics.cs |
| Rendering/DiagnosticCollector.cs | Rendering/DiagnosticCollector.cs |
| Rendering/DiagnosticSeverity.cs | Rendering/DiagnosticSeverity.cs |
| Rendering/DiagnosticStage.cs | Rendering/DiagnosticStage.cs |
| Rendering/ImageResources.cs | Rendering/ImageResources.cs |
| Slim専用の新規実装 | ConversionDiagnostic.cs |
| Slim専用の新規実装 | ExcelConverter.cs |
| Rendering/WorkbookInputOptions.cs | WorkbookInputOptions.cs |
| Slim専用の新規実装 | PdfExportOptions.cs |
| Slim専用の新規実装 | ConversionResult.cs |

</details>

ヘッダー/フッターは既存のフィールド置換・配置を継承します。Excel独自の書式制御コードを新たに解析する処理は追加していません。LinuxのSkia native層は初期化時にfontconfigへアクセスする場合があります。指定フォント以外のfaceは選択しませんが、native層の初期化アクセスが皆無とは主張しません。
