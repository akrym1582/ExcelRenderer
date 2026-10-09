# Core source provenance and final ownership

Basic algorithms are owned by Core. Public main types remain in ExcelRenderer; the retained implementation column distinguishes compatibility mapping from product policy.
Source: main 1.7.2, commit `32e9165d145516dbd0bcb4c2ae31a78ce6774bce`. Every copied type is internal; namespaces are under ExcelRenderer.Core. This table describes lineage, not identical copies.

| Source under src/ExcelRenderer | Destination under src/ExcelRenderer.Core | Final owner | Retained implementation / reason |
| --- | --- | --- | --- |
| Abstractions/IReportLayoutPass.cs | Abstractions/IReportLayoutPass.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Abstractions/ITextLayoutService.cs | Abstractions/ITextLayoutService.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Abstractions/ITextMeasurer.cs | Abstractions/ITextMeasurer.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Abstractions/TextLayoutLine.cs | Abstractions/TextLayoutLine.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Abstractions/TextLayoutResult.cs | Abstractions/TextLayoutResult.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Abstractions/TextSize.cs | Abstractions/TextSize.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Compatibility/IsExternalInit.cs | Compatibility/IsExternalInit.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Drawing/BorderStrokeGeometry.cs | Drawing/BorderStrokeGeometry.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Drawing/DrawBorderCommand.cs | Drawing/DrawBorderCommand.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Drawing/DrawCommand.cs | Drawing/DrawCommand.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Drawing/DrawCommandGeneratorPass.cs | Drawing/DrawCommandGeneratorPass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Drawing/DrawImageCommand.cs | Drawing/DrawImageCommand.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Drawing/DrawLineCommand.cs | Drawing/DrawLineCommand.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Drawing/DrawTextCommand.cs | Drawing/DrawTextCommand.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Drawing/FillRectangleCommand.cs | Drawing/FillRectangleCommand.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Drawing/PositionedTextLine.cs | Drawing/PositionedTextLine.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Drawing/TextLayoutFontSize.cs | Drawing/TextLayoutFontSize.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Drawing/TextLayoutPlacement.cs | Drawing/TextLayoutPlacement.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Excel/CellRangeIndex.cs | Excel/CellRangeIndex.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Excel/ColumnWidthCalculator.cs | Excel/ColumnWidthCalculator.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Excel/DrawingMLReader.cs | Excel/DrawingMLReader.cs | Core単独所有 / 本体専用差分 | Shared anchor parsing; full reader enriches basic picture metadata with crop/rotation/flip and reads shapes |
| Excel/DrawingPictureMetadata.cs | Excel/DrawingPictureMetadata.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Excel/ExcelReader.cs | Excel/CoreExcelReader.cs | Core単独所有 / 本体wrapper | Common traversal and merged/dimension reading; full hooks retain style/shapes/links; public dictionary projection preserves source order |
| Excel/ExcelStyleConverter.cs | Excel/ExcelStyleConverter.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Excel/NormalFontMetadata.cs | Excel/NormalFontMetadata.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Excel/RawColumnDefinition.cs | Excel/RawColumnDefinition.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Excel/RawRowDefinition.cs | Excel/RawRowDefinition.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Excel/SheetPageSetupMetadata.cs | Excel/SheetPageSetupMetadata.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| New Core implementation | Excel/SingleFontGraphicEngine.cs | Core単独所有 | Complete; Core single-font product implementation |
| Excel/StylePool.cs | Excel/StylePool.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Excel/WorkbookLayoutMetadataReader.cs | Excel/WorkbookLayoutMetadataReader.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| New Core implementation | Fonts/SingleFontContext.cs | Core単独所有 | Complete; Core single-font product implementation |
| New Core implementation | Fonts/SingleFontResolver.cs | Core単独所有 | Complete; Core single-font product implementation |
| GlobalUsings.cs | GlobalUsings.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Rendering/BufferReadStream.cs | Input/BufferReadStream.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Rendering/CancellationReadStream.cs | Input/CancellationReadStream.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Rendering/RenderBufferOptions.cs | Input/InputBufferOptions.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Rendering/PreparedWorkbook.cs | Input/PreparedWorkbook.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Rendering/SpillableBufferStream.cs | Input/SpillableBufferStream.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Rendering/WorkbookInputPreparer.cs | Input/WorkbookInputPreparer.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Layout/BandIndex.cs | Layout/BandIndex.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Layout/CellBoundsPass.cs | Layout/CellBoundsPass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/CellContentBounds.cs | Layout/CellContentBounds.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Layout/CellLayout.cs | Layout/CellLayout.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/ColumnLayout.cs | Layout/ColumnLayout.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/ColumnLayoutPass.cs | Layout/ColumnLayoutPass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/DrawingAnchorResolver.cs | Layout/DrawingAnchorResolver.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/HeaderFooterLayout.cs | Layout/HeaderFooterLayout.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/HiddenRowColumnPass.cs | Layout/HiddenRowColumnPass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/NormalizePass.cs | Layout/NormalizePass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/ObjectGeometry.cs | Layout/ObjectGeometry.cs | Core単独所有 / 本体専用差分 | Shared rotation/source bounds; full shape stroke protrusions remain in main |
| Layout/PageBand.cs | Layout/PageBand.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/PageBandBuilder.cs | Layout/PageBandBuilder.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Layout/PageCellSelection.cs | Layout/PageCellSelection.cs | Core単独所有 / 本体専用差分 | Core origin/title selection; FullCellSelection adds requested-range merged intersections |
| Layout/PagePlacement.cs | Layout/PagePlacement.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Layout/PaginationPagePlan.cs | Layout/PaginationPagePlan.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/PaginationPass.cs | Layout/PaginationPass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/PrintScaleResolver.cs | Layout/PrintScaleResolver.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Layout/RectangleGeometry.cs | Layout/RectangleGeometry.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/RenderBorder.cs | Layout/RenderBorder.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/RenderCell.cs | Layout/RenderCell.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/RenderImage.cs | Layout/RenderImage.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/RenderPage.cs | Layout/RenderPage.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/RenderPageBuilder.cs | Layout/RenderPageBuilder.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/RenderText.cs | Layout/RenderText.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/ReportLayoutContext.cs | Layout/ReportLayoutContext.cs | 本体専用差分 / Core内部型 | Public mutable compatibility context remains in main; adapters synchronize only the fields owned by an independently executed pass |
| Layout/ReportLayoutEngine.cs | Layout/ReportLayoutEngine.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/ReportRect.cs | Layout/ReportRect.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/ResolvePrintAreaPass.cs | Layout/ResolvePrintAreaPass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/RowLayout.cs | Layout/RowLayout.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Layout/RowLayoutPass.cs | Layout/RowLayoutPass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/SheetGeometry.cs | Layout/SheetGeometry.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Layout/SheetLayoutPlan.cs | Layout/SheetLayoutPlan.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/SheetObjectLayoutIndex.cs | Layout/SheetObjectLayoutIndex.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/TextLayoutTransform.cs | Layout/TextLayoutTransform.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Layout/TextMeasurePass.cs | Layout/TextMeasurePass.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Model/BorderLineStyle.cs | Model/BorderLineStyle.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/BorderSide.cs | Model/BorderSide.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/BorderStyle.cs | Model/BorderStyle.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/CellAddress.cs | Model/CellAddress.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/CellBorder.cs | Model/CellBorder.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/CellRange.cs | Model/CellRange.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/CellStyle.cs | Model/CellStyle.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/ColumnDefinition.cs | Model/ColumnDefinition.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/DrawingAnchor.cs | Model/DrawingAnchor.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| New (base-character normalization) | Model/DisplayText.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/FontStyle.cs | Model/FontStyle.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/HeaderFooter.cs | Model/HeaderFooter.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/HeaderFooterSection.cs | Model/HeaderFooterSection.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/HorizontalAlignment.cs | Model/HorizontalAlignment.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/IndexRange.cs | Model/IndexRange.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/PageSettings.cs | Model/PageSettings.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/PrintPageOrder.cs | Model/PrintPageOrder.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/PrintScaleMode.cs | Model/PrintScaleMode.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/ReportCell.cs | Model/ReportCell.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/ReportColor.cs | Model/ReportColor.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/ReportDocument.cs | Model/ReportDocument.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/ReportImage.cs | Model/ReportImage.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/ReportSheet.cs | Model/ReportSheet.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/RowDefinition.cs | Model/RowDefinition.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| Model/VerticalAlignment.cs | Model/VerticalAlignment.cs | 本体専用差分 / Core内部型 | Public compatibility value/contracts stay in main; neutral internal equivalents stay in Core |
| PdfSharp/PdfSharpFinalizedTextPainter.cs | Pdf/PdfSharpFinalizedTextPainter.cs | Core単独所有 / 本体専用差分 | Core owns basic PDF drawing; full font painters remain in main |
| PdfSharp/PdfSharpRenderer.cs | Pdf/CorePdfRenderer.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| PdfSharp/PdfSharpTextMeasurer.cs | Pdf/PdfSharpTextMeasurer.cs | Core単独所有 / 本体専用差分 | Core owns basic PDF drawing; full font painters remain in main |
| New Core implementation | Properties/AssemblyInfo.cs | Core単独所有 | Complete; Core single-font product implementation |
| New Core implementation | Rendering/CancellationWriteStream.cs | Core単独所有 | Complete; Core single-font product implementation |
| Rendering/ConversionDiagnostic.cs | Rendering/ConversionDiagnostic.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Rendering/ConversionMetrics.cs | Rendering/ConversionMetrics.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| Rendering/DiagnosticCollector.cs | Rendering/DiagnosticCollector.cs | Core単独所有 / 本体wrapper | Shared Core implementation; main delegates and maps only boundary state |
| Rendering/DiagnosticSeverity.cs | Rendering/DiagnosticSeverity.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Rendering/DiagnosticStage.cs | Rendering/DiagnosticStage.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| Rendering/ImageResources.cs | Rendering/ImageResources.cs | Core単独所有 / 本体wrapper | Complete; shared implementation, public metadata mapping where needed |
| New Core implementation | ConversionDiagnostic.cs | Core単独所有 | Complete; Core single-font product implementation |
| New Core implementation | ExcelConverter.cs | Core単独所有 | Complete; Core single-font product implementation |
| Rendering/WorkbookInputOptions.cs | WorkbookInputOptions.cs | 本体専用差分 / Core内部型 | Compatibility value types retained in main; neutral Core values and explicit adapters do not duplicate algorithms |
| New Core implementation | PdfExportOptions.cs | Core単独所有 | Complete; Core single-font product implementation |
| New Core implementation | ConversionResult.cs | Core単独所有 | Complete; Core single-font product implementation |

FontManager, font runs/glyph special cases, shape/link models and renderers, API range/viewport/continuous orchestration, output sinks/formats, Markdown and Skia output renderers remain main-only. Core DrawingMLReader owns picture anchors and Z order; main enriches those anchors with shape/theme/crop/rotation metadata. PdfSharpTextMeasurer and FinalizedTextPainter use one shared regular XFont per size; reader also supplies a SingleFontGraphicEngine to ClosedXML to avoid its default system-font lookup. InputBufferOptions contains only input spool controls.

Tests adapted from PdfContentProbe, DrawingAnchorWorkbookFixture and PaginationComponentTests; SampleInputs and Noto fixtures are reused without product embedding. Additional Core tests and validation harnesses are new.

Fix basic algorithms in Core and run both product regressions. CoreLayoutPolicy/FullLayoutPolicy own the intentional image origin/intersection and feature policy differences. Continuous canvas orchestration remains in main and delegates cell geometry, finalized placement and cell-layer commands to Core.


## Extension and lifetime boundaries

- `FullExcelReader` retains original styles/display/source geometry in the reader hooks before Core normalizes its single-font product.
- `FullLayoutPolicy` supplies shape/image visual bounds before used-range and pagination resolution, selection rules, transformed images, shapes and source regions. Core owns band planning, candidate extraction, measurements and placement.
- `CoreTextLayoutAdapter` stores finalized full runs in `FullLineData`; Core scales neutral metrics and a payload scale, while main restores run offsets without searching for fonts or rewrapping.
- `ICoreDrawingExtension` supplies the shared image/shape Z stage. Background/border/merged-border/text layer generation is Core-only, including continuous rescans.
- `FullPdfRenderer` paints full finalized/legacy runs, transformed images, shapes and viewports; its page-completion hook prepares annotations for execution after all destination pages exist. Core owns page append, basic paint, image decoding and final Save.
- Normal conversion reads neutral cells directly. Public sheet views borrow those dictionaries, range filters preserve neutral values, and internal page building goes directly to common command generation. Public `ExcelReader.Read` materializes durable public dictionaries for its compatibility contract.
- Typed payloads borrow image bytes, runs and metadata; conversion sessions own native resources. PDFsharp font snapshots use weak registration entries backed by the lifetime of their original resolved fonts, allowing public finalized runs to remain renderable without keeping every conversion's bytes globally.
