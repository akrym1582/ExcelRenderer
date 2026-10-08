# Slim source provenance

Source: main 1.7.2, commit `32e9165d145516dbd0bcb4c2ae31a78ce6774bce`. Every copied type is internal; namespaces are under ExcelRenderer.Slim. This table describes lineage, not identical copies.

| Source under src/ExcelRenderer | Destination under src/ExcelRenderer.Slim |
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
| New Slim implementation | Excel/SingleFontGraphicEngine.cs |
| Excel/StylePool.cs | Excel/StylePool.cs |
| Excel/WorkbookLayoutMetadataReader.cs | Excel/WorkbookLayoutMetadataReader.cs |
| New Slim implementation | Fonts/SingleFontContext.cs |
| New Slim implementation | Fonts/SingleFontResolver.cs |
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
| New (base-character normalization) | Model/DisplayText.cs |
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
| New Slim implementation | Properties/AssemblyInfo.cs |
| New Slim implementation | Rendering/CancellationWriteStream.cs |
| Rendering/ConversionDiagnostic.cs | Rendering/ConversionDiagnostic.cs |
| Rendering/ConversionMetrics.cs | Rendering/ConversionMetrics.cs |
| Rendering/DiagnosticCollector.cs | Rendering/DiagnosticCollector.cs |
| Rendering/DiagnosticSeverity.cs | Rendering/DiagnosticSeverity.cs |
| Rendering/DiagnosticStage.cs | Rendering/DiagnosticStage.cs |
| Rendering/ImageResources.cs | Rendering/ImageResources.cs |
| New Slim implementation | SlimDiagnostic.cs |
| New Slim implementation | SlimExcelConverter.cs |
| Rendering/WorkbookInputOptions.cs | SlimInputOptions.cs |
| New Slim implementation | SlimPdfOptions.cs |
| New Slim implementation | SlimPdfResult.cs |

FontManager, font runs/glyph special cases, shape/link models and renderers, API range/viewport/continuous layout, output sinks/formats, Markdown and Skia output renderers were not retained. DrawingMLReader retains only picture anchors and Z order. PdfSharpTextMeasurer and FinalizedTextPainter use one shared regular XFont per size; reader also supplies a SingleFontGraphicEngine to ClosedXML to avoid its default system-font lookup. InputBufferOptions contains only input spool controls.

Tests adapted from PdfContentProbe, DrawingAnchorWorkbookFixture and PaginationComponentTests; SampleInputs and Noto fixtures are reused without product embedding. Additional Slim tests and validation harnesses are new.

For shared fixes, inspect and test both the original and Slim implementations. Existing source projects have not been reorganized.
