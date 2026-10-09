using ClosedXML.Excel;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Extends the common reader with full style, drawings and hyperlink metadata.</summary>
internal sealed class FullExcelReader : CoreExcelReader
{
    private readonly DiagnosticCollector? diagnostics;

    /// <summary>Initializes a new instance of the <see cref="FullExcelReader"/> class.</summary>
    /// <param name="fontManager">The optional full font manager.</param>
    /// <param name="diagnostics">The existing diagnostic collector.</param>
    /// <param name="maximumDigitWidths">The public reader's metric cache.</param>
    internal FullExcelReader(IFontManager? fontManager, DiagnosticCollector? diagnostics, Dictionary<NormalFontMetadata, double> maximumDigitWidths)
        : base(new FullFontMetrics(fontManager, diagnostics, maximumDigitWidths)) => this.diagnostics = diagnostics;

    /// <inheritdoc/>
    protected override void PrepareWorkbookMetadata(Core.Input.PreparedWorkbook source, XLWorkbook workbook, CoreWorkbookMetadata metadata, IReadOnlyList<string>? selectedSheets)
    {
        using var drawingStream = source.OpenRead();
        using var pictureStream = source.OpenRead();
        using var hyperlinkStream = source.OpenRead();
        var unselectedGeometry = new Dictionary<string, IReadOnlyList<Model.CellRange>>(StringComparer.Ordinal);
        var shapes = Excel.DrawingMLReader.Read(drawingStream, diagnostics, selectedSheets, unselectedGeometry);
        var pictures = Excel.DrawingMLReader.ReadPictureMetadata(pictureStream, metadata.Pictures);
        var hyperlinks = Excel.HyperlinkReader.Read(hyperlinkStream);
        metadata.IncludeUnselectedSheetGeometry = true;
        metadata.ExtensionData = new FullWorkbookData(shapes, pictures, hyperlinks);
        foreach (var sheet in workbook.Worksheets)
        {
            metadata.AdditionalGeometry[sheet.Name] = unselectedGeometry.GetValueOrDefault(sheet.Name, [])
                .Concat(shapes.GetValueOrDefault(sheet.Name, []).SelectMany(shape => AnchorRanges(shape.Anchor, shape.DrawingAnchor)))
                .Select(CoreModelAdapter.ToCore).ToArray();
        }
    }

    /// <inheritdoc/>
    protected override bool ShouldReadCell(IXLCell cell, CoreSheetReadContext context)
    {
        var metadata = GetHyperlinks(cell.Worksheet.Name, context);
        var address = new Model.CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber);
        return metadata is null || metadata.OriginalCells.Contains(address) || !metadata.Links.Any(link => link.SourceRange.Contains(address));
    }

    /// <inheritdoc/>
    protected override string ReadDisplayText(IXLCell cell, CoreSheetReadContext context) =>
        GetHyperlinks(cell.Worksheet.Name, context)?.UncachedDisplays.GetValueOrDefault(new Model.CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber)) ?? base.ReadDisplayText(cell, context);

    /// <inheritdoc/>
    protected override CellStyle ConvertCellStyle(IXLCell cell) => Core.Excel.ExcelStyleConverter.ConvertOriginal(cell);

    /// <inheritdoc/>
    protected override string NormalizeDisplayText(string text) => text;

    /// <inheritdoc/>
    protected override ReportSheet CompleteSheet(IXLWorksheet worksheet, ReportSheet sheet, CoreSheetReadContext context)
    {
        var data = (FullWorkbookData)context.Metadata.ExtensionData!;
        var pictures = data.Pictures.GetValueOrDefault(worksheet.Name);
        return sheet with
        {
            Images = sheet.Images?.Select(image => image.Name is { } name && pictures?.GetValueOrDefault(name) is { } picture
                ? image with { ExtensionData = new FullPictureData(picture) } : image).ToArray(),
            ExtensionData = new FullSheetData(context.IncludeBody ? data.Shapes.GetValueOrDefault(worksheet.Name, []) : [], data.Hyperlinks.GetValueOrDefault(worksheet.Name)),
        };
    }

    private static Excel.SheetHyperlinkMetadata? GetHyperlinks(string sheetName, CoreSheetReadContext context) =>
        ((FullWorkbookData)context.Metadata.ExtensionData!).Hyperlinks.GetValueOrDefault(sheetName);

    private static IEnumerable<Model.CellRange> AnchorRanges(Model.CellAddress fallback, Model.DrawingAnchor? anchor)
    {
        var from = anchor?.From ?? fallback;
        var to = anchor?.To ?? from;
        yield return new(
            new(Math.Min(from.Row, to.Row), Math.Min(from.Column, to.Column)),
            new(Math.Max(from.Row, to.Row), Math.Max(from.Column, to.Column)));
    }
}
