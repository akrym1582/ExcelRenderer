using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Rendering;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

/// <summary>Separates original workbook column metrics from substituted drawing fonts.</summary>
public sealed class ColumnMetricTests
{
    [Theory]
    [InlineData(false, 219.75)]
    [InlineData(true, 276.5472666)]
    public async Task LibreOffice_width_mode_preserves_fractional_cell_size_and_saved_row_height(bool compatible, double expected)
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            TestSupport.Sheet(workbook).InsertAt(new S.Columns(new S.Column { Min = 1, Max = 1, Width = 41.901101, CustomWidth = true }), 0);
            var row = TestSupport.Sheet(workbook).GetFirstChild<S.SheetData>()!.GetFirstChild<S.Row>()!;
            row.Height = 120;
            row.CustomHeight = true;
        });
        var options = new ExcelReadOptions { UseLibreOfficeColumnWidths = compatible, MaximumDigitWidthPixels96Dpi = compatible ? 8.8 : null };
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        var sheet = new CoreExcelReader(font, null, options).Read(prepared, null).Sheets.Single();
        Assert.InRange(Math.Abs(sheet.Columns[1].Width - expected), 0, 0.000001);
        Assert.Equal(120, sheet.Rows[1].Height);
    }

    [Fact]
    public void LibreOffice_metrics_retain_twip_precision_for_the_selected_font()
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var metrics = new SingleFontMetrics(font, null, new() { UseLibreOfficeColumnWidths = true });
        Assert.Equal(122d / 15, metrics.MaximumDigitWidth(new("Calibri", 11), "First"));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.5)]
    public async Task Invalid_explicit_column_metrics_fail_before_output(double width)
    {
        using var input = TestSupport.Workbook();
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ExcelConverter.ConvertAsync(input, output, TestSupport.Options with { ReadOptions = new() { MaximumDigitWidthPixels96Dpi = width } }));
        Assert.Equal(0, output.Length);
    }

    /// <summary>Legacy Japanese forms use the same substitution metrics as the reference Calc import.</summary>
    /// <param name="family">A supported source-family spelling.</param>
    /// <param name="size">The source Normal size.</param>
    /// <param name="expected">The reference digit width at 96 DPI.</param>
    [Theory]
    [InlineData("ＭＳ Ｐゴシック", 11, 8.4)]
    [InlineData("MS PGothic", 11, 8.4)]
    [InlineData("ms pgothic", 10, 7.6)]
    [InlineData("MS Pゴシック", 20, 229d / 15)]
    public async Task Missing_ms_p_gothic_uses_Calc_compatible_source_metrics(string family, double size, double expected)
    {
        foreach (var path in new[] { TestSupport.JapaneseFont, TestSupport.MonoFont })
        {
            using var input = TestSupport.Workbook(workbook =>
            {
                var normal = workbook.WorkbookStylesPart!.Stylesheet.Fonts!.GetFirstChild<S.Font>()!;
                normal.FontName = new() { Val = family };
                normal.FontSize = new() { Val = size };
                TestSupport.Sheet(workbook).InsertAt(new S.Columns(new S.Column { Min = 1, Max = 39, Width = 3.12, CustomWidth = true }), 0);
            });
            using var font = new SingleFontContext(path);
            var diagnostics = new DiagnosticCollector();
            Assert.Equal(expected, new SingleFontMetrics(font, diagnostics).MaximumDigitWidth(new(family, size), "First"));
            using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
            var sheet = new CoreExcelReader(font, diagnostics).Read(prepared, diagnostics).Sheets.Single();
            if (size == 11)
            {
                // Calc's ODS export records 0.2728in (19.6416pt) for this column.
                TestSupport.Near(19.5, sheet.Columns[1].Width);
                Assert.InRange(Math.Abs(sheet.Columns[1].Width - 19.6416), 0, 0.15);
            }

            Assert.Contains(diagnostics.ToArray(), diagnostic => diagnostic.Code == "MaximumDigitWidthFallback");
        }
    }

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
