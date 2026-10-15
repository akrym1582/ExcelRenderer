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
    private readonly bool detectUnsupportedFeatures;

    /// <summary>Initializes a new instance of the <see cref="FullExcelReader"/> class.</summary>
    /// <param name="fontManager">The optional full font manager.</param>
    /// <param name="diagnostics">The existing diagnostic collector.</param>
    /// <param name="maximumDigitWidths">The public reader's metric cache.</param>
    /// <param name="detectUnsupportedFeatures">Whether to retain additional unsupported-feature candidates.</param>
    internal FullExcelReader(IFontManager? fontManager, DiagnosticCollector? diagnostics, Dictionary<NormalFontMetadata, double> maximumDigitWidths, bool detectUnsupportedFeatures = false)
        : base(new FullFontMetrics(fontManager, diagnostics, maximumDigitWidths))
    {
        this.diagnostics = diagnostics;
        this.detectUnsupportedFeatures = detectUnsupportedFeatures;
    }

    /// <inheritdoc/>
    protected override void PrepareWorkbookMetadata(Core.Input.PreparedWorkbook source, XLWorkbook workbook, CoreWorkbookMetadata metadata, IReadOnlyList<string>? selectedSheets)
    {
        using var drawingStream = source.OpenRead();
        using var pictureStream = source.OpenRead();
        using var hyperlinkStream = source.OpenRead();
        var unselectedGeometry = new Dictionary<string, IReadOnlyList<Model.CellRange>>(StringComparer.Ordinal);
        var unsupported = new Dictionary<string, IReadOnlyList<UnsupportedFeature>>(StringComparer.Ordinal);
        var shapes = Excel.DrawingMLReader.Read(drawingStream, diagnostics, selectedSheets, unselectedGeometry, detectUnsupportedFeatures ? unsupported : null);
        var pictures = Excel.DrawingMLReader.ReadPictureMetadata(pictureStream, metadata.Pictures);
        var hyperlinks = Excel.HyperlinkReader.Read(hyperlinkStream);
        metadata.IncludeUnselectedSheetGeometry = true;
        metadata.ExtensionData = new FullWorkbookData(shapes, pictures, hyperlinks) { UnsupportedFeatures = unsupported };
        foreach (var sheet in workbook.Worksheets)
        {
            metadata.AdditionalGeometry[sheet.Name] = unselectedGeometry.GetValueOrDefault(sheet.Name, [])
                .Concat(shapes.GetValueOrDefault(sheet.Name, []).SelectMany(shape => AnchorRanges(shape.Anchor, shape.DrawingAnchor)))
                .Concat(unsupported.GetValueOrDefault(sheet.Name, []).SelectMany(feature => feature.Ranges))
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
    protected override ReportCell CompleteCell(IXLCell source, ReportCell cell, CoreSheetReadContext context)
    {
        if (source.HasFormula || !source.HasRichText)
        {
            return cell;
        }

        var runs = new List<Model.CellTextRun>();
        var unsupported = source.GetRichText().HasPhonetics;
        foreach (var run in source.GetRichText())
        {
            unsupported |= run.Strikethrough || run.VerticalAlignment != XLFontVerticalTextAlignmentValues.Baseline ||
                run.Underline is not (XLFontUnderlineValues.None or XLFontUnderlineValues.Single);
            var text = run.Text;
            if (text.Length == 0)
            {
                continue;
            }

            var color = Core.Excel.ExcelStyleConverter.ToColor(run.FontColor, source.Worksheet.Workbook.Theme);
            var font = new Model.FontStyle(run.FontName, run.FontSize, run.Bold, run.Italic, run.Underline == XLFontUnderlineValues.Single, color is { } value ? CoreModelAdapter.ToPublic(value) : null);
            if (runs.Count > 0 && runs[runs.Count - 1].Font == font)
            {
                runs[runs.Count - 1] = runs[runs.Count - 1] with { Text = runs[runs.Count - 1].Text + text };
            }
            else
            {
                runs.Add(new(text, font));
            }
        }

        for (var index = 0; index < runs.Count; index++)
        {
            var text = runs[index].Text.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (text.EndsWith("\r", StringComparison.Ordinal) && index + 1 < runs.Count && runs[index + 1].Text.StartsWith("\n", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1) + "\n";
                runs[index + 1] = runs[index + 1] with { Text = runs[index + 1].Text.Substring(1) };
            }

            runs[index] = runs[index] with { Text = text };
        }

        runs.RemoveAll(run => run.Text.Length == 0);
        unsupported |= runs.Count > 1 && (cell.Style.TextRotation != 0 || cell.Style.TopToBottom);
        return cell with
        {
            Text = string.Concat(runs.Select(run => run.Text)),
            ExtensionData = !unsupported && (runs.Count == 0 || (runs.Count == 1 && runs[0].Font == CoreModelAdapter.ToPublic(cell.Style.Font)))
                ? null : new FullCellTextData(runs.AsReadOnly()) { UnsupportedFormatting = unsupported },
        };
    }

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
            ExtensionData = new FullSheetData(context.IncludeBody ? data.Shapes.GetValueOrDefault(worksheet.Name, []) : [], data.Hyperlinks.GetValueOrDefault(worksheet.Name))
            {
                UnsupportedFeatures = context.IncludeBody && detectUnsupportedFeatures ? data.UnsupportedFeatures.GetValueOrDefault(worksheet.Name, []).Concat(worksheet.ConditionalFormats.Select((rule, index) => new UnsupportedFeature(
                    "UnsupportedConditionalFormatting",
                    "A conditional formatting rule exists but its visual effects are not rendered.",
                    "conditional-format-" + (index + 1),
                    rule.Ranges.Select(range => new Model.CellRange(new(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber), new(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber))).ToArray()))).ToArray() : [],
            },
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
