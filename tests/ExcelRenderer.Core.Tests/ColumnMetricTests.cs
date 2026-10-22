using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Rendering;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

/// <summary>Separates original workbook column metrics from substituted drawing fonts.</summary>
public sealed class ColumnMetricTests
{
    /// <summary>Replacing Calibri must not enlarge the workbook's saved column widths.</summary>
    /// <param name="sourceFamily">The source Normal family.</param>
    [Theory]
    [InlineData("Calibri")]
    [InlineData("Unavailable source font")]
    public async Task Substituted_fonts_keep_compatibility_column_widths_and_report_fallback(string sourceFamily)
    {
        foreach (var path in new[] { TestSupport.JapaneseFont, TestSupport.MonoFont })
        {
            using var input = TestSupport.Workbook(workbook =>
            {
                workbook.WorkbookStylesPart!.Stylesheet.Fonts!.GetFirstChild<S.Font>()!.FontName = new() { Val = sourceFamily };
                TestSupport.Sheet(workbook).InsertAt(new S.Columns(new S.Column { Min = 1, Max = 1, Width = 10, CustomWidth = true }), 0);
            });
            using var font = new SingleFontContext(path);
            var diagnostics = new DiagnosticCollector();
            using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
            var sheet = new CoreExcelReader(font, diagnostics).Read(prepared, diagnostics).Sheets.Single();
            TestSupport.Near(52.5, sheet.Columns[1].Width);
            var warning = Assert.Single(diagnostics.ToArray(), d => d.Code == "MaximumDigitWidthFallback");
            Assert.Equal("First", warning.SheetName);
            Assert.Contains(sourceFamily, warning.Message);
        }
    }

    /// <summary>Matching physical families retain font measurement at the source Normal size.</summary>
    [Fact]
    public void Matching_source_family_uses_the_supplied_snapshot_without_a_fallback_warning()
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var diagnostics = new DiagnosticCollector();
        var metrics = new SingleFontMetrics(font, diagnostics);
        Assert.Equal(8, metrics.MaximumDigitWidth(new("noto sans jp", 11), "First"));
        Assert.Equal(15, metrics.MaximumDigitWidth(new("Noto Sans JP", 20), "First"));
        Assert.Empty(diagnostics.ToArray());
    }

    /// <summary>Missing source metadata must never select a drawing substitute's wider metrics.</summary>
    [Fact]
    public void Missing_Normal_metadata_uses_a_diagnosed_compatibility_width()
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var diagnostics = new DiagnosticCollector();
        Assert.Equal(7, new SingleFontMetrics(font, diagnostics).MaximumDigitWidth(null, "First"));
        Assert.Contains("unknown", Assert.Single(diagnostics.ToArray()).Message);
    }
}
