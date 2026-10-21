using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using PreparedWorkbook = ExcelRenderer.OpenXml.Reader.Input.PreparedWorkbook;

namespace ExcelRenderer;

/// <summary>Determines formula targets using the same value-independent geometry as rendering.</summary>
public static partial class ExcelConverter
{
    private static ExcelReadOptions SelectFormulaPageTargets(PreparedWorkbook source, RenderRequest request, ExcelReadOptions options, FontManager fonts, CancellationToken token)
    {
        if ((options.FormulaDisplayMode != FormulaDisplayMode.Recalculate && !options.ApplyConditionalFormatting) || request.Selection.Pages is null)
        {
            return options;
        }

        // Saved values are read only to create a geometry model. Pagination uses source
        // addresses, saved row/column dimensions and object bounds, never cell text.
        // The real read below calculates the exact candidates before formatting them.
        var geometryDocument = new ExcelReader(fonts).ReadCore(source, null, request.Selection.SheetNames, request.DiagnosticOptions.DetectUnsupportedFeatures, options with { FormulaDisplayMode = FormulaDisplayMode.CachedValue, ApplyConditionalFormatting = false }, token);
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
        var appearanceTargets = new Dictionary<string, List<Func<int, int, bool>>>(StringComparer.OrdinalIgnoreCase);
        var targetBoundaries = new Dictionary<string, (HashSet<int> Rows, HashSet<int> Columns)>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            token.ThrowIfCancellationRequested();
            if (!targetBoundaries.TryGetValue(page.Sheet.Name, out var boundaries))
            {
                boundaries = ([], []);
                targetBoundaries.Add(page.Sheet.Name, boundaries);
            }

            if (!targets.TryGetValue(page.Sheet.Name, out var addresses))
            {
                addresses = [];
                targets.Add(page.Sheet.Name, addresses);
            }

            if (page.Plan is { } plan)
            {
                var pageAddresses = plan.GetCandidateAddresses(page.PlanIndex);
                addresses.UnionWith(pageAddresses);
                var bands = plan.Pages[page.PlanIndex];
                if (bands.Horizontal is { } horizontal && bands.Vertical is { } vertical)
                {
                    var geometry = new Layout.SheetGeometry(page.Sheet).CoreGeometry;

                    // Include both sides of geometry transitions and singleton saved/merged anchors.
                    foreach (var position in new[] { vertical.Start, vertical.End, bands.TitleRowEnd })
                    {
                        var row = geometry.RowAt(position);
                        boundaries.Rows.UnionWith([row, row + 1]);
                    }

                    foreach (var position in new[] { horizontal.Start, horizontal.End, bands.TitleColumnEnd })
                    {
                        var column = geometry.ColumnAt(position);
                        boundaries.Columns.UnionWith([column, column + 1]);
                    }

                    foreach (var row in bands.TitleRows)
                    {
                        boundaries.Rows.UnionWith([row, row + 1]);
                    }

                    foreach (var column in bands.TitleColumns)
                    {
                        boundaries.Columns.UnionWith([column, column + 1]);
                    }

                    foreach (var address in pageAddresses)
                    {
                        boundaries.Rows.Add(address.Row);
                        boundaries.Rows.Add(address.Row + 1);
                        boundaries.Columns.Add(address.Column);
                        boundaries.Columns.Add(address.Column + 1);
                    }

                    var selection = new CoreIntegration.FullCellSelection(
                        new(horizontal.Start, horizontal.End),
                        new(vertical.Start, vertical.End),
                        bands.TitleColumns.ToHashSet(),
                        bands.TitleRows.ToHashSet(),
                        bands.TitleColumnEnd,
                        bands.TitleRowEnd,
                        page.Sheet.RequestedRange is not null);
                    if (!appearanceTargets.TryGetValue(page.Sheet.Name, out var predicates))
                    {
                        predicates = [];
                        appearanceTargets.Add(page.Sheet.Name, predicates);
                    }

                    predicates.Add((row, column) =>
                    {
                        var address = new Core.Model.CellAddress(row, column);
                        var bounds = Core.Layout.RectangleGeometry.Bounds(geometry, new(address, address));
                        return bounds.Width > 0 && bounds.Height > 0 && selection.Contains(address, bounds, false);
                    });
                }
            }
        }

        return options with
        {
            ConditionalTargetBoundaries = sheet => targetBoundaries.TryGetValue(sheet, out var boundaries) ? (boundaries.Rows.ToArray(), boundaries.Columns.ToArray()) : (Array.Empty<int>(), Array.Empty<int>()),
            IsFormulaTarget = (sheet, row, column) => targets.TryGetValue(sheet, out var addresses) && addresses.Contains(new(row, column)),
            IsConditionalFormattingTarget = (sheet, row, column) =>
                (targets.TryGetValue(sheet, out var addresses) && addresses.Contains(new(row, column))) ||
                (appearanceTargets.TryGetValue(sheet, out var predicates) && predicates.Any(predicate => predicate(row, column))),
        };
    }
}
