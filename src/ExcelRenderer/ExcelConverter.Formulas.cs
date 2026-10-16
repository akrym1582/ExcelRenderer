using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer;

/// <summary>Determines formula targets using the same value-independent geometry as rendering.</summary>
public static partial class ExcelConverter
{
    private static ExcelReadOptions SelectFormulaPageTargets(PreparedWorkbook source, RenderRequest request, ExcelReadOptions options, FontManager fonts, CancellationToken token)
    {
        if (options.FormulaDisplayMode != FormulaDisplayMode.Recalculate || request.Selection.Pages is null)
        {
            return options;
        }

        // Saved values are read only to create a geometry model. Pagination uses source
        // addresses, saved row/column dimensions and object bounds, never cell text.
        // The real read below calculates the exact candidates before formatting them.
        var geometryDocument = new ExcelReader(fonts).ReadCore(source, null, request.Selection.SheetNames, request.DiagnosticOptions.DetectUnsupportedFeatures, options with { FormulaDisplayMode = FormulaDisplayMode.CachedValue, BackendSelected = null }, token);
        var styles = new StylePool();
        var document = new ReportDocument(geometryDocument.Sheets.Select(sheet => CoreIntegration.CoreModelAdapter.ToPublic(sheet, styles)).ToArray());
        var geometryDiagnostics = new DiagnosticCollector(request.DiagnosticOptions);
        var sheets = ApplyRanges(SelectSheets(document, request.Selection.SheetNames), request.Selection, geometryDiagnostics);
        if (request.DiagnosticOptions.DetectUnsupportedFeatures)
        {
            sheets = sheets.Where(selected => !selected.Sheet.UnsupportedFeatures.Any(feature => feature.IsSheetLevel)).ToArray();
        }

        sheets = sheets.Select(selected => selected with { Sheet = ProjectSheet(selected.Sheet, request) }).ToArray();
        var pages = SelectPages(LayoutPages(sheets, request.Dpi, fonts), request.Selection.Pages);
        var targets = new Dictionary<string, HashSet<CellAddress>>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            token.ThrowIfCancellationRequested();
            if (!targets.TryGetValue(page.Sheet.Name, out var addresses))
            {
                addresses = [];
                targets.Add(page.Sheet.Name, addresses);
            }

            if (page.Plan is { } plan)
            {
                addresses.UnionWith(plan.GetCandidateAddresses(page.PlanIndex));
            }
        }

        return options with { IsFormulaTarget = (sheet, row, column) => targets.TryGetValue(sheet, out var addresses) && addresses.Contains(new(row, column)) };
    }
}
