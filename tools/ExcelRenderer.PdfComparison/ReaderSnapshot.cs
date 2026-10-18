using System.Text.Json;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.PdfComparison;

/// <summary>Captures source layout inputs separately from finalized drawing fingerprints.</summary>
internal static class ReaderSnapshot
{
    /// <summary>Reads dimensions, merges, rich runs and resolved MDW diagnostics without native handles.</summary>
    /// <param name="path">The fixture workbook.</param>
    /// <param name="fonts">The fixed physical font policy.</param>
    /// <returns>The deterministic input projection.</returns>
    internal static JsonElement Capture(string path, IFontManager fonts)
    {
        var diagnostics = new DiagnosticCollector(new());
        var document = new ExcelReader(fonts).Read(File.ReadAllBytes(path), diagnostics);
        var styles = document.Sheets.SelectMany(sheet => sheet.Cells.Values).Select(cell => cell.Style).Distinct().ToArray();
        var styleIndexes = styles.Select((style, index) => (style, index)).ToDictionary(item => item.style, item => item.index);
        return JsonSerializer.SerializeToElement(new
        {
            styles,
            dimensionBandSchema = new[] { "first", "last", "sizePoints", "hidden01" },
            sheets = document.Sheets.Select(sheet => new
            {
                sheet.Name,
                sheet.DefaultColumnWidth,
                sheet.DefaultRowHeight,
                sheet.DefaultRowsHidden,
                columns = DimensionBands(sheet.Columns.Select(item => (item.Key, item.Value.Width, item.Value.IsHidden))),
                rows = DimensionBands(sheet.Rows.Select(item => (item.Key, item.Value.Height, item.Value.IsHidden))),
                sheet.MergedRanges,
                sheet.PrintArea,
                sheet.PrintAreas,
                sheet.PageSettings,
                sheet.HeaderFooter,
                cells = sheet.Cells.OrderBy(item => item.Key.Row).ThenBy(item => item.Key.Column).Select(item => new
                {
                    address = item.Key,
                    item.Value.Text,
                    item.Value.Formula,
                    styleIndex = styleIndexes[item.Value.Style],
                    item.Value.RowSpan,
                    item.Value.ColumnSpan,
                    item.Value.RichTextRuns,
                }),
            }),
            mdw = diagnostics.ToArray().Where(diagnostic => diagnostic.Code.StartsWith("MaximumDigitWidth", StringComparison.Ordinal)),
        });
    }

    private static IReadOnlyList<double[]> DimensionBands(IEnumerable<(int Index, double Size, bool Hidden)> dimensions)
    {
        var bands = new List<double[]>();
        foreach (var item in dimensions.OrderBy(item => item.Index))
        {
            var hidden = item.Hidden ? 1d : 0;
            if (bands.Count > 0 && bands[^1][1] + 1 == item.Index && bands[^1][2] == item.Size && bands[^1][3] == hidden)
            {
                bands[^1][1] = item.Index;
            }
            else
            {
                bands.Add([item.Index, item.Index, item.Size, hidden]);
            }
        }

        return bands;
    }
}
