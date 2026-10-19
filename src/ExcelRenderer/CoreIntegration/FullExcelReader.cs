using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Extends the common reader with full style, drawings and hyperlink metadata.</summary>
internal sealed partial class FullExcelReader : CoreExcelReader
{
    private readonly DiagnosticCollector? diagnostics;
    private readonly bool detectUnsupportedFeatures;
    private readonly Excel.ExcelReadOptions readOptions;

    /// <summary>Initializes a new instance of the <see cref="FullExcelReader"/> class.</summary>
    /// <param name="fontManager">The optional full font manager.</param>
    /// <param name="diagnostics">The existing diagnostic collector.</param>
    /// <param name="maximumDigitWidths">The public reader's metric cache.</param>
    /// <param name="detectUnsupportedFeatures">Whether to retain additional unsupported-feature candidates.</param>
    /// <param name="readOptions">The product formula and selection policy.</param>
    internal FullExcelReader(IFontManager? fontManager, DiagnosticCollector? diagnostics, Dictionary<NormalFontMetadata, double> maximumDigitWidths, bool detectUnsupportedFeatures = false, Excel.ExcelReadOptions? readOptions = null)
        : base(new FullFontMetrics(fontManager, diagnostics, maximumDigitWidths))
    {
        this.diagnostics = diagnostics;
        this.detectUnsupportedFeatures = detectUnsupportedFeatures;
        this.readOptions = readOptions ?? new Excel.ExcelReadOptions();
    }

    /// <inheritdoc/>
    protected override bool CaptureRichText => true;

    /// <inheritdoc/>
    protected override bool CaptureConditionalFormatting => detectUnsupportedFeatures;

    /// <inheritdoc/>
    protected override void PrepareWorkbookMetadata(Core.Input.PreparedWorkbook source, IReadOnlyList<string> sheetNames, CoreWorkbookMetadata metadata, IReadOnlyList<string>? selectedSheets)
    {
        using var drawingStream = source.OpenRead();
        using var hyperlinkStream = source.OpenRead();
        var unselectedGeometry = new Dictionary<string, IReadOnlyList<Model.CellRange>>(StringComparer.Ordinal);
        var unsupported = new Dictionary<string, IReadOnlyList<UnsupportedFeature>>(StringComparer.Ordinal);
        var shapes = Excel.DrawingMLReader.Read(drawingStream, diagnostics, selectedSheets, unselectedGeometry, detectUnsupportedFeatures ? unsupported : null);
        var hyperlinks = Excel.HyperlinkReader.Read(hyperlinkStream, selectedSheets);
        if (detectUnsupportedFeatures)
        {
            using var chartStream = source.OpenRead();
            using var package = SpreadsheetDocument.Open(chartStream, false);
            var workbookPart = package.WorkbookPart!;
            var index = 0;
            foreach (var sheet in workbookPart.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
            {
                var name = sheet.Name!.Value!;
                metadata.SourceSheetIndices[name] = ++index;
                if (workbookPart.GetPartById(sheet.Id!) is ChartsheetPart &&
                    (sheet.State is null || sheet.State.Value == DocumentFormat.OpenXml.Spreadsheet.SheetStateValues.Visible) &&
                    (selectedSheets is null || selectedSheets.Contains(name, StringComparer.Ordinal)))
                {
                    metadata.AdditionalSheets.Add(new(name, new Dictionary<CellAddress, ReportCell>(), new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new())
                    {
                        SourceSheetIndex = index,
                        ExtensionData = new FullSheetData([], null)
                        {
                            UnsupportedFeatures = [new("UnsupportedDrawingObject", "A chart sheet is not rendered; cell position is unavailable.", "chart-sheet", []) { IsSheetLevel = true }],
                        },
                    });
                }
            }
        }

        metadata.IncludeUnselectedSheetGeometry = true;
        metadata.ExtensionData = new FullWorkbookData(shapes, hyperlinks) { UnsupportedFeatures = unsupported };
        foreach (var name in sheetNames)
        {
            metadata.AdditionalGeometry[name] = unselectedGeometry.GetValueOrDefault(name, [])
                .Concat(shapes.GetValueOrDefault(name, []).SelectMany(shape => AnchorRanges(shape.Anchor, shape.DrawingAnchor)))
                .Concat(unsupported.GetValueOrDefault(name, []).SelectMany(feature => feature.Ranges))
                .Select(CoreModelAdapter.ToCore).ToArray();
        }
    }

    /// <inheritdoc/>
    protected override ReportCell CompleteCell(SourceCellText? source, ReportCell cell, CoreSheetReadContext context) => source is null ? cell :
        CompleteRichText(cell, source.Runs.Select(run => new Model.CellTextRun(run.Text, CoreModelAdapter.ToPublic(run.Font))).ToList(), source.UnsupportedFormatting);

    /// <inheritdoc/>
    protected override string NormalizeDisplayText(string text) => text;

    private static ReportCell CompleteRichText(ReportCell cell, List<Model.CellTextRun> runs, bool unsupported)
    {
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
        for (var index = runs.Count - 1; index > 0; index--)
        {
            if (runs[index - 1].Font == runs[index].Font)
            {
                runs[index - 1] = runs[index - 1] with { Text = runs[index - 1].Text + runs[index].Text };
                runs.RemoveAt(index);
            }
        }

        unsupported |= runs.Any(run => run.Font != CoreModelAdapter.ToPublic(cell.Style.Font)) &&
            (cell.Style.TextRotation != 0 || cell.Style.TopToBottom);
        return cell with
        {
            Text = string.Concat(runs.Select(run => run.Text)),
            ExtensionData = !unsupported && (runs.Count == 0 || (runs.Count == 1 && runs[0].Font == CoreModelAdapter.ToPublic(cell.Style.Font)))
                ? null : new FullCellTextData(runs.AsReadOnly()) { UnsupportedFormatting = unsupported },
        };
    }

    private static IEnumerable<Model.CellRange> AnchorRanges(Model.CellAddress fallback, Model.DrawingAnchor? anchor)
    {
        var from = anchor?.From ?? fallback;
        var to = anchor?.To ?? from;
        yield return new(
            new(Math.Min(from.Row, to.Row), Math.Min(from.Column, to.Column)),
            new(Math.Max(from.Row, to.Row), Math.Max(from.Column, to.Column)));
    }
}
