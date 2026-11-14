using DocumentFormat.OpenXml.Packaging;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("pdf")]
    [InlineData("svg")]
    [InlineData("image")]
    [InlineData("ps")]
    [InlineData("render")]
    [InlineData("core")]
    public async Task Column_metrics_options_are_forwarded_to_every_render_command(string command)
    {
        var output = Path.Combine(_directory, command == "pdf" ? "metrics.pdf" : command == "ps" ? "metrics.ps" : "metrics");
        var args = new List<string> { command, Input, "-o", output, "--column-width-mdw", "16", "--column-width-fallback-mdw", "7.5", "--column-width-fallback-metrics", "font", "--column-width-metrics", "source", "--layout-font", Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", "collection-distinct.ttc"), "--layout-font-index", "1" };
        if (command == "render") args.AddRange(["--format", "svg"]);
        if (command == "core") args.AddRange(["--font-file", CoreFont]);
        AssertSuccess(await RunAsync(args.ToArray()));
    }

    [Theory]
    [InlineData("--column-width-mdw", "0")]
    [InlineData("--column-width-mdw", "NaN")]
    [InlineData("--column-width-fallback-mdw", "0")]
    [InlineData("--column-width-fallback-mdw", "NaN")]
    [InlineData("--column-width-fallback-metrics", "invalid")]
    [InlineData("--column-width-metrics", "invalid")]
    [InlineData("--layout-font-index", "1")]
    [InlineData("--layout-font", "missing.ttf")]
    public async Task Invalid_column_metrics_fail_with_clear_errors(string option, string value)
    {
        var result = await RunAsync("pdf", Input, "-o", Path.Combine(_directory, "invalid.pdf"), option, value);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_directory, "invalid.pdf")));
    }

    [Theory]
    [InlineData("core", null, null, 8, "fallback-override")]
    [InlineData("core", "font", null, 12, "drawing-font")]
    [InlineData("core", "font", 7.5, 7.5, "fallback-override")]
    [InlineData("render", null, null, 8, "fallback-override")]
    [InlineData("render", "font", null, 12, "drawing-font")]
    [InlineData("render", "font", 7.5, 7.5, "fallback-override")]
    public async Task Column_fallback_defaults_to_8px_and_font_measurement_requires_an_option(string command, string? mode, double? width, double expected, string source)
    {
        var input = CreateMultiSheetInput();
        var output = Path.Combine(_directory, "fallback.pdf");
        var font = Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", "collection-distinct.ttc");
        var args = new List<string> { command, input, "-o", output, "--font-file", font, "--no-system-fonts", "--verbose" };
        if (command == "core") args.AddRange(["--font-collection-index", "1"]);
        else args.AddRange(["--format", "pdf", "--font-face-index", "1", "--font-policy", "requested", "--fallback-font", "PDF Format Fixture"]);
        if (mode is not null) args.AddRange(["--column-width-fallback-metrics", mode]);
        if (width is not null) args.AddRange(["--column-width-fallback-mdw", width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        var result = await RunAsync(args.ToArray());
        AssertSuccess(result);
        Assert.Contains($"MDW {expected.ToString(System.Globalization.CultureInfo.InvariantCulture)}px", result.Error);
        Assert.Contains("source=" + source, result.Error);
    }

    [Fact]
    public async Task Core_rejects_invalid_fallback_metrics_without_output()
    {
        var output = Path.Combine(_directory, "invalid.pdf");
        var result = await RunAsync("core", Input, "-o", output, "--font-file", CoreFont, "--column-width-fallback-metrics", "invalid");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--column-width-fallback-metrics must be fixed or font", result.Error);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("core", "source", null, 12, "supplied-font")]
    [InlineData("render", "source", null, 12, "exact-font")]
    [InlineData("core", "fallback", null, 8, "fallback-override")]
    [InlineData("render", "fallback", null, 8, "fallback-override")]
    [InlineData("core", "fallback", "font", 12, "drawing-font")]
    [InlineData("render", "fallback", "font", 12, "drawing-font")]
    public async Task Fallback_mode_ignores_an_available_matching_source_font(string command, string metrics, string? fallbackMetrics, double expected, string source)
    {
        var input = CreateMultiSheetInput();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.AddNewPart<WorkbookStylesPart>().Stylesheet = new S.Stylesheet(
                new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "PDF Format Fixture" })),
                new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None })),
                new S.Borders(new S.Border()),
                new S.CellStyleFormats(new S.CellFormat()),
                new S.CellFormats(new S.CellFormat()),
                new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
        }

        var output = Path.Combine(_directory, "source-policy.pdf");
        var font = Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", "collection-distinct.ttc");
        var args = new List<string> { command, input, "-o", output, "--font-file", font, "--no-system-fonts", "--column-width-metrics", metrics, "--verbose" };
        if (command == "core") args.AddRange(["--font-collection-index", "1"]);
        else args.AddRange(["--format", "pdf", "--font-face-index", "1", "--font-policy", "requested", "--fallback-font", "PDF Format Fixture"]);
        if (fallbackMetrics is not null) args.AddRange(["--column-width-fallback-metrics", fallbackMetrics]);
        var result = await RunAsync(args.ToArray());
        AssertSuccess(result);
        Assert.Contains($"MDW {expected.ToString(System.Globalization.CultureInfo.InvariantCulture)}px", result.Error);
        Assert.Contains("source=" + source, result.Error);
        if (metrics == "fallback") Assert.Contains("Source-font lookup is disabled", result.Error);
    }
}
