using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.PdfComparison;

/// <summary>Captures source layout inputs separately from finalized drawing fingerprints.</summary>
internal static class ReaderSnapshot
{
    /// <summary>Gets the metric policy used by the immutable parent PDF fixtures.</summary>
    internal static ExcelReadOptions HistoricalReadOptions { get; } = new()
    {
        ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy },
    };

    /// <summary>Reads dimensions, merges, rich runs and resolved MDW diagnostics without native handles.</summary>
    /// <param name="path">The fixture workbook.</param>
    /// <param name="fonts">The fixed physical font policy.</param>
    /// <param name="diagnosticsPath">Destination for the unmodified current metric diagnostics.</param>
    /// <returns>The deterministic input projection.</returns>
    internal static JsonElement Capture(string path, IFontManager fonts, string diagnosticsPath)
    {
        var diagnostics = new DiagnosticCollector(new());
        var document = new ExcelReader(fonts).Read(File.ReadAllBytes(path), diagnostics, HistoricalReadOptions);
        var metrics = diagnostics.ToArray().Where(diagnostic => diagnostic.Code.StartsWith("MaximumDigitWidth", StringComparison.Ordinal)).ToArray();
        File.WriteAllText(diagnosticsPath, JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
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
            mdw = HistoricalDiagnostics(metrics),
        });
    }

    /// <summary>Projects current diagnostics to the frozen legacy schema without changing metric values.</summary>
    /// <param name="diagnostics">Unmodified current diagnostics, also retained separately.</param>
    /// <returns>The historical diagnostic projection.</returns>
    internal static IReadOnlyList<ConversionDiagnostic> HistoricalDiagnostics(IReadOnlyList<ConversionDiagnostic> diagnostics)
    {
        var result = new List<ConversionDiagnostic>();
        var resolved = new HashSet<ConversionDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Code == "SavedPageBreaks" && diagnostic.Severity == DiagnosticSeverity.Info && diagnostic.Stage == DiagnosticStage.Read)
            {
                // This new informational summary has no counterpart in the frozen schema.
                // Source break metadata and final page bands are still compared independently.
                continue;
            }

            if (diagnostic.Code.StartsWith("MaximumDigitWidth", StringComparison.Ordinal))
            {
                // The historical reader logged each successful Normal resolution once per workbook;
                // current diagnostics additionally report its reuse on every sheet.
                if (diagnostic.Code != "MaximumDigitWidthResolved" || resolved.Add(diagnostic with { SheetName = null }))
                {
                    result.Add(HistoricalMetricDiagnostic(diagnostic));
                }
            }
            else
            {
                result.Add(diagnostic);
            }
        }

        return result;
    }

    private static ConversionDiagnostic HistoricalMetricDiagnostic(ConversionDiagnostic diagnostic)
    {
        // Keep the frozen projection's prose schema while comparing the actual requested/resolved
        // family, MDW, severity and location. Preserve the full current diagnostics separately.
        var requested = Regex.Match(diagnostic.Message, "^Normal '(?<family>[^']+)', ");
        var width = Regex.Match(diagnostic.Message, @", MDW (?<width>[0-9.]+)px at 96 DPI; source=legacy\.");
        if (!requested.Success || !width.Success)
        {
            throw new InvalidOperationException("Unexpected legacy MDW diagnostic format: " + diagnostic.Message);
        }

        var family = requested.Groups["family"].Value;
        if (diagnostic.Code == "MaximumDigitWidthFallback" && width.Groups["width"].Value == "7" && diagnostic.Message.Contains("7px compatibility metric", StringComparison.Ordinal))
        {
            return diagnostic with { Message = $"Normal font '{family}' could not be measured; the 7px compatibility metric was used." };
        }

        var resolved = Regex.Match(diagnostic.Message, "measurement '(?<family>[^,']+), ");
        if (diagnostic.Code != "MaximumDigitWidthResolved" || !resolved.Success)
        {
            throw new InvalidOperationException("Unexpected legacy MDW resolution: " + diagnostic.Message);
        }

        return diagnostic with { Message = $"Normal font '{family}' resolved to '{resolved.Groups["family"].Value}' with MDW {width.Groups["width"].Value}px." };
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
