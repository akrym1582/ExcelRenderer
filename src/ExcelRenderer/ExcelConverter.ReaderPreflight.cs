using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using PdfSharp.Fonts;

namespace ExcelRenderer;

/// <summary>Applies existing page geometry and diagnostic filtering to Auto reader preflight.</summary>
public static partial class ExcelConverter
{
    private static IReadOnlyList<string> InspectReaderCandidate(Core.Model.ReportDocument candidate, RenderRequest request, FontManager fonts, RenderResourceSession resources, CancellationToken token)
    {
        var styles = new Excel.StylePool();
        var document = new ReportDocument(candidate.Sheets.Select(sheet => CoreIntegration.CoreModelAdapter.ToPublic(sheet, styles)).ToArray());
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new DiagnosticCollector(new() { MaxDiagnostics = 1 }, diagnostic =>
        {
            if (diagnostic.Stage == DiagnosticStage.Read && diagnostic.Code is not "FormulaCachedValueMissing" and not "FormulaResultError")
            {
                reasons.Add(diagnostic.Code);
            }
        });
        var sheets = ApplyRanges(SelectSheets(document, request.Selection.SheetNames), request.Selection, diagnostics)
            .Where(selected => !selected.Sheet.UnsupportedFeatures.Any(feature => feature.IsSheetLevel))
            .Select(selected => selected with { Sheet = ProjectSheet(selected.Sheet, request) }).ToArray();
        var borrowed = resources.FontSession;
        using var lease = borrowed is null ? Core.Rendering.PdfSharpFontGate.Acquire(token) : null;
        resources.FontSession = borrowed ?? lease;
        try
        {
            if (borrowed is null)
            {
                GlobalFontSettings.ResetFontManagement();
                GlobalFontSettings.FontResolver = new PdfSharpFontResolver(fonts);
            }

            var pages = request.ImageLayout == ImageLayoutMode.Continuous
                ? LayoutContinuous(sheets, request.Dpi, fonts)
                : LayoutPages(sheets, request.Dpi, fonts);
            foreach (var page in SelectPages(pages, request.Selection.Pages))
            {
                token.ThrowIfCancellationRequested();
                var payload = BuildPage(page, fonts, token);
                CollectCellReadDiagnostics(page.Sheet, payload.Regions, page.Descriptor.SourcePageNumber, diagnostics);
            }

            return Array.AsReadOnly(reasons.OrderBy(code => code, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            resources.FontSession = borrowed;
            if (borrowed is null)
            {
                GlobalFontSettings.ResetFontManagement();
            }
        }
    }
}
