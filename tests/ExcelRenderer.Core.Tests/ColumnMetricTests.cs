using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

/// <summary>Separates original workbook column metrics from substituted drawing fonts.</summary>
public sealed class ColumnMetricTests
{
    [Theory]
    [InlineData(false, 251.25)]
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
        var options = new ExcelReadOptions { UseSystemFontsForColumnWidths = false, UseLibreOfficeColumnWidths = compatible, MaximumDigitWidthPixels96Dpi = compatible ? 8.8 : null };
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
        var metrics = new SingleFontMetrics(font, null, new() { UseLibreOfficeColumnWidths = true, FallbackMaximumDigitWidthPixels96Dpi = null });
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

    [Theory]
    [InlineData("ＭＳ Ｐゴシック", 11, 8, 9)]
    [InlineData("MS PGothic", 10, 7, 8)]
    [InlineData("游ゴシック", 11, 8, 9)]
    [InlineData("Yu Gothic", 20, 15, 16)]
    [InlineData("Unavailable source font", 11, 8, 9)]
    public async Task Missing_source_uses_the_drawing_font_at_Normal_size(string family, double size, double japaneseWidth, double monoWidth)
    {
        foreach (var (path, expected) in new[] { (TestSupport.JapaneseFont, japaneseWidth), (TestSupport.MonoFont, monoWidth) })
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
            var options = new ExcelReadOptions { UseSystemFontsForColumnWidths = false, FallbackMaximumDigitWidthPixels96Dpi = null };
            Assert.Equal(expected, new SingleFontMetrics(font, diagnostics, options).MaximumDigitWidth(new(family, size), "First"));
            using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
            var sheet = new CoreExcelReader(font, diagnostics, options).Read(prepared, diagnostics).Sheets.Single();
            Assert.Equal(ColumnWidthCalculator.ToPoints(3.12, expected), sheet.Columns[1].Width);
            Assert.All(diagnostics.ToArray().Where(diagnostic => diagnostic.Code == "MaximumDigitWidthFallback"), diagnostic =>
            {
                Assert.Equal("MaximumDigitWidthFallback", diagnostic.Code);
                Assert.Contains("source=drawing-font", diagnostic.Message);
                Assert.Contains(font.Face.Typeface.FamilyName, diagnostic.Message);
            });
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
        Assert.All(diagnostics.ToArray(), diagnostic => Assert.Equal("MaximumDigitWidthResolved", diagnostic.Code));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(11)]
    [InlineData(20)]
    public void Missing_source_defaults_to_8px_independently_of_drawing_font_and_Normal_size(double size)
    {
        Assert.Equal(8, new ExcelReadOptions().FallbackMaximumDigitWidthPixels96Dpi);
        foreach (var path in new[] { TestSupport.JapaneseFont, TestSupport.MonoFont })
        {
            using var font = new SingleFontContext(path);
            var diagnostics = new DiagnosticCollector();
            var metrics = new SingleFontMetrics(font, diagnostics, new() { UseSystemFontsForColumnWidths = false });
            Assert.Equal(8, metrics.MaximumDigitWidth(new("Unavailable source font", size), "First"));
            Assert.Contains("source=fallback-override", Assert.Single(diagnostics.ToArray()).Message);
        }
    }

    [Theory]
    [InlineData(8d, null, 8)]
    [InlineData(null, null, 16)]
    [InlineData(7.5, null, 7.5)]
    [InlineData(8d, 12.5, 12.5)]
    public void Fallback_only_mode_skips_source_matching_and_system_lookup(double? fallback, double? forced, double expected)
    {
        using var font = new SingleFontContext(TestSupport.MonoFont);
        foreach (var family in new[] { "Noto Sans Mono", "Noto Sans JP" })
        {
            var diagnostics = new DiagnosticCollector();
            var options = new ExcelReadOptions { UseSourceFontForColumnWidths = false, FallbackMaximumDigitWidthPixels96Dpi = fallback, MaximumDigitWidthPixels96Dpi = forced };
            var metrics = new SingleFontMetrics(font, diagnostics, options, _ => throw new InvalidOperationException("Source lookup must not run."));
            Assert.Equal(expected, metrics.MaximumDigitWidth(new(family, 20), "First"));
            var diagnostic = Assert.Single(diagnostics.ToArray());
            Assert.Contains("Source-font lookup is disabled", diagnostic.Message);
            Assert.Equal(forced is null ? "MaximumDigitWidthFallback" : "MaximumDigitWidthResolved", diagnostic.Code);
        }
    }

    [Fact]
    public async Task Fallback_only_mode_rejects_an_explicit_source_file_without_output()
    {
        using var input = TestSupport.Workbook();
        using var output = new MemoryStream();
        var options = TestSupport.Options with { ReadOptions = new() { UseSourceFontForColumnWidths = false, SourceFontFile = TestSupport.JapaneseFont } };
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.ConvertAsync(input, output, options));
        Assert.Equal(0, output.Length);
    }

    /// <summary>Missing source metadata uses the fixed default unless drawing-font measurement is requested.</summary>
    [Fact]
    public void Missing_Normal_metadata_defaults_to_fixed_width_and_can_measure_the_drawing_font()
    {
        using var font = new SingleFontContext(TestSupport.MonoFont);
        var diagnostics = new DiagnosticCollector();
        Assert.Equal(8, new SingleFontMetrics(font, diagnostics).MaximumDigitWidth(null, "First"));
        Assert.Contains("unknown", Assert.Single(diagnostics.ToArray()).Message);
        Assert.Equal(9, new SingleFontMetrics(font, null, new() { FallbackMaximumDigitWidthPixels96Dpi = null }).MaximumDigitWidth(null, "First"));
    }

    [Theory]
    [InlineData(true, 9, "system-font")]
    [InlineData(false, 8, "drawing-font")]
    public void Original_system_font_is_measured_separately_from_the_drawing_font(bool allowSystem, double expected, string source)
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var diagnostics = new DiagnosticCollector();
        var options = new ExcelReadOptions { UseSystemFontsForColumnWidths = allowSystem, FallbackMaximumDigitWidthPixels96Dpi = allowSystem ? 17 : null };
        var lookups = 0;
        var metrics = new SingleFontMetrics(font, diagnostics, options, normal =>
        {
            lookups++;
            Assert.Equal("Noto Sans Mono", normal.Family);
            return SKTypeface.FromFile(TestSupport.MonoFont);
        });
        Assert.Equal(expected, metrics.MaximumDigitWidth(new("Noto Sans Mono", 11), "First"));
        Assert.Equal(allowSystem ? 1 : 0, lookups);
        Assert.Contains("source=" + source, Assert.Single(diagnostics.ToArray()).Message);
    }

    [Theory]
    [InlineData("missing", null, 8d)]
    [InlineData("substituted", null, 8d)]
    [InlineData("unreadable", 7.5, 7.5)]
    public void Unavailable_or_unrelated_system_faces_use_the_requested_fallback(string result, double? fallback, double expected)
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var diagnostics = new DiagnosticCollector();
        var options = new ExcelReadOptions { FallbackMaximumDigitWidthPixels96Dpi = fallback };
        var metrics = new SingleFontMetrics(font, diagnostics, options, _ => result switch
        {
            "missing" => null,
            "substituted" => SKTypeface.FromFile(TestSupport.JapaneseFont),
            _ => throw new IOException("Source font is unavailable."),
        });
        Assert.Equal(expected, metrics.MaximumDigitWidth(new("Noto Sans Mono", 11), "First"));
        Assert.Equal("MaximumDigitWidthFallback", Assert.Single(diagnostics.ToArray()).Code);
    }

    [Fact]
    public void Native_system_lookup_handles_the_fonts_available_on_this_host()
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var family = SKFontManager.Default.FontFamilies.FirstOrDefault(candidate => !ColumnFontMetrics.MatchesFamily(font.Face.Typeface, candidate));
        var normal = new NormalFontMetadata(family ?? "Unavailable source font", 11);
        using var source = SKFontManager.Default.MatchFamily(normal.Family);
        var available = family is not null && source is not null && ColumnFontMetrics.MatchesFamily(source, family);
        var expected = available ? ColumnFontMetrics.Measure(source!, 11) : 17;
        var diagnostics = new DiagnosticCollector();
        var metrics = new SingleFontMetrics(font, diagnostics, new() { FallbackMaximumDigitWidthPixels96Dpi = 17 });
        Assert.Equal(expected, metrics.MaximumDigitWidth(normal, "First"));
        Assert.Contains(available ? "source=system-font" : "source=fallback-override", Assert.Single(diagnostics.ToArray()).Message);
    }

    [Theory]
    [InlineData(null, null, 8, "drawing-font")]
    [InlineData(7.5, null, 7.5, "fallback-override")]
    [InlineData(7.5, 12.5, 12.5, "override")]
    public void Fallback_override_is_used_only_when_source_metrics_are_unavailable(double? fallback, double? forced, double expected, string source)
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var diagnostics = new DiagnosticCollector();
        var options = new ExcelReadOptions { FallbackMaximumDigitWidthPixels96Dpi = fallback, MaximumDigitWidthPixels96Dpi = forced };
        var metrics = new SingleFontMetrics(font, diagnostics, options);
        Assert.Equal(expected, metrics.MaximumDigitWidth(new("Missing Original", 11), "First"));
        Assert.Contains("source=" + source, Assert.Single(diagnostics.ToArray()).Message);
        Assert.Equal(forced ?? 8, metrics.MaximumDigitWidth(new("Noto Sans JP", 11), "Second"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Explicit_source_collection_face_overrides_system_and_drawing_fonts(int index)
    {
        using var drawing = new SingleFontContext(TestSupport.JapaneseFont);
        var path = Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", "collection-distinct.ttc");
        using var source = new SingleFontContext(path, index);
        var options = new ExcelReadOptions { SourceFontFile = path, SourceFontCollectionIndex = index, FallbackMaximumDigitWidthPixels96Dpi = 17 };
        var diagnostics = new DiagnosticCollector();
        var metrics = new SingleFontMetrics(drawing, diagnostics, options);
        Assert.Equal(source.MaximumDigitWidth(20), metrics.MaximumDigitWidth(new("Missing Original", 20), "First"));
        Assert.Contains("source=source-file", Assert.Single(diagnostics.ToArray()).Message);
    }

    [Theory]
    [InlineData(false, null, 730.8)]
    [InlineData(true, null, 730.8)]
    [InlineData(false, 7d, 630)]
    [InlineData(true, 7d, 630)]
    public async Task Default_and_explicit_columns_keep_the_42_column_form_width(bool explicitColumns, double? fallback, double expected)
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            workbook.WorkbookStylesPart!.Stylesheet.Fonts!.GetFirstChild<S.Font>()!.FontName = new() { Val = "游ゴシック" };
            var sheet = TestSupport.Sheet(workbook);
            sheet.InsertAt(new S.SheetFormatProperties { DefaultColumnWidth = 3.58203125, DefaultRowHeight = 18 }, 0);
            if (explicitColumns)
            {
                sheet.InsertAt(new S.Columns(new S.Column { Min = 1, Max = 42, Width = 3.58203125, CustomWidth = true }), 1);
            }

            sheet.GetFirstChild<S.SheetData>()!.GetFirstChild<S.Row>()!.Append(TestWorkbook.Text("AP1", "■"));
            sheet.Append(new S.PageSetup { PaperSize = 9, Orientation = S.OrientationValues.Landscape, Scale = 80 });
        });
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        var options = new ExcelReadOptions { UseSystemFontsForColumnWidths = false, FallbackMaximumDigitWidthPixels96Dpi = fallback };
        var sheet = new CoreExcelReader(font, null, options).Read(prepared, null).Sheets.Single();
        var plan = Assert.Single(new ReportLayoutEngine(new RangeOnlyMeasurer()).Plan(sheet));
        var page = Assert.Single(plan.Pages);
        Assert.Equal(0.8, page.Scale);
        Assert.Equal(expected, Enumerable.Range(1, 42).Sum(column => sheet.Columns.TryGetValue(column, out var definition) ? definition.Width : sheet.DefaultColumnWidth) * page.Scale, 8);
        Assert.Equal(18, sheet.DefaultRowHeight);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.5)]
    public async Task Invalid_fallback_width_fails_before_output(double width)
    {
        using var input = TestSupport.Workbook();
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ExcelConverter.ConvertAsync(input, output, TestSupport.Options with { ReadOptions = new() { FallbackMaximumDigitWidthPixels96Dpi = width } }));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("missing.ttf", -1)]
    [InlineData("missing.ttf", 0)]
    public async Task Invalid_source_font_settings_fail_without_writing_output(string? path, int index)
    {
        using var input = TestSupport.Workbook();
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ExcelConverter.ConvertAsync(input, output, TestSupport.Options with { ReadOptions = new() { SourceFontFile = path, SourceFontCollectionIndex = index, MaximumDigitWidthPixels96Dpi = 9 } }));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(1, "游ゴシック")]
    [InlineData(16, "Localized Family")]
    public void Localized_physical_family_names_are_verified_from_the_name_table(int nameId, string family)
    {
        var name = System.Text.Encoding.BigEndianUnicode.GetBytes(family);
        var table = new byte[18 + name.Length];
        table[3] = 1;
        table[5] = 18;
        table[7] = 3;
        table[13] = (byte)nameId;
        table[15] = (byte)name.Length;
        name.CopyTo(table, 18);
        Assert.True(ColumnFontMetrics.HasFamilyName(table, family.ToUpperInvariant()));
        Assert.False(ColumnFontMetrics.HasFamilyName(table, "Different Family"));
        table[13] = 2;
        Assert.False(ColumnFontMetrics.HasFamilyName(table, family));
        Assert.False(ColumnFontMetrics.HasFamilyName([], family));
        table[13] = (byte)nameId;
        table[17] = 255;
        Assert.False(ColumnFontMetrics.HasFamilyName(table, family));
    }
}
