using Xunit;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class MultipleFontTests
{
    private static FontOptions Fonts() => new()
    {
        Files = [TestSupport.JapaneseFont, TestSupport.MonoFont],
        FamilyAliases = { ["游ゴシック"] = "Noto Sans JP", ["游明朝"] = "Noto Sans Mono" },
        SansSerifFallback = "Noto Sans JP",
        SerifFallback = "Noto Sans Mono",
        DefaultFamily = "Noto Sans JP",
    };

    [Theory]
    [InlineData("Noto Sans Mono", 2, "Noto Sans Mono")]
    [InlineData("游明朝", 2, "Noto Sans Mono")]
    [InlineData("游ゴシック", 1, "Noto Sans JP")]
    [InlineData("Missing", 1, "Noto Sans Mono")]
    [InlineData("Missing", 2, "Noto Sans JP")]
    [InlineData("Missing", 0, "Noto Sans JP")]
    [InlineData("Missing", 3, "Noto Sans JP")]
    [InlineData("noto sans mono", 0, "Noto Sans Mono")]
    public void Family_selection_prefers_exact_then_alias_then_classification_then_default(string family, int classification, string expected)
    {
        using var fonts = new SingleFontContext(Fonts());
        var face = fonts.Resolve(family, classification);
        Assert.Equal(expected, face.Typeface.FamilyName);
        Assert.Same(face, fonts.Resolve(family, classification));
        Assert.Same(face, fonts.Resolve(face.Identity));
    }

    [Fact]
    public void Aliases_are_case_insensitive_and_do_not_count_as_original_metrics()
    {
        var options = Fonts();
        options.FamilyAliases.Add("Alias", "Noto Sans Mono");
        options.FamilyAliases.Add("Noto Sans JP", "Noto Sans Mono");
        using var fonts = new SingleFontContext(options);
        Assert.Equal("Noto Sans Mono", fonts.Resolve("ALIAS").Typeface.FamilyName);
        Assert.Equal("Noto Sans JP", fonts.Resolve("Noto Sans JP").Typeface.FamilyName);
        Assert.Null(fonts.FindExact("Alias"));
        var source = new SingleFontMetrics(fonts, options: new() { UseSystemFontsForColumnWidths = false });
        Assert.Equal(8, source.MaximumDigitWidth(new("Alias", 11), "First"));
        var fallback = new SingleFontMetrics(fonts, options: new()
        {
            UseSourceFontForColumnWidths = false,
            FallbackMaximumDigitWidthPixels96Dpi = null,
        });
        Assert.Equal(ColumnFontMetrics.Measure(fonts.Resolve("Alias").Typeface, 11), fallback.MaximumDigitWidth(new("Alias", 11), "First"));
        Assert.Equal(ColumnFontMetrics.Measure(fonts.Resolve("Missing", 1).Typeface, 11), fallback.MaximumDigitWidth(new("Missing", 11) { FamilyClassification = 1 }, "First"));
        Assert.Equal(ColumnFontMetrics.Measure(fonts.Resolve("Noto Sans Mono").Typeface, 11), source.MaximumDigitWidth(new("Noto Sans Mono", 11), "First"));
    }

    [Fact]
    public void Layout_cache_and_wrapping_use_the_selected_physical_face()
    {
        using var fonts = new SingleFontContext(Fonts());
        var measurer = new SkiaTextMeasurer(fonts);
        var sans = new FontStyle("Noto Sans JP", 11);
        var mono = new FontStyle("Noto Sans Mono", 11);
        var first = measurer.Layout("iiii WWWW", sans, 100, false);
        var second = measurer.Layout("iiii WWWW", mono, 100, false);
        Assert.NotEqual(first.Size.Width, second.Size.Width);
        Assert.NotEqual(first.Lines[0].Glyphs!.FontIdentity, second.Lines[0].Glyphs!.FontIdentity);
        Assert.Same(first, measurer.Layout("iiii WWWW", sans, 100, false));
        foreach (var font in new[] { sans, mono })
        {
            var wrapped = measurer.Layout("iiii WWWW iiiii WWWW", font, 36, true);
            Assert.True(wrapped.Lines.Count > 1);
            Assert.All(wrapped.Lines, line => Assert.Equal(fonts.Resolve(font.Family).Identity, line.Glyphs!.FontIdentity));
            Assert.All(wrapped.Lines, line => Assert.InRange(line.Width, 0, 36));
        }
    }

    [Fact]
    public async Task Workbook_classification_and_aliases_reach_pdf_with_both_fonts_embedded()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var styles = workbook.WorkbookStylesPart!.Stylesheet;
            styles.Fonts!.Append(
                new S.Font(new S.FontName { Val = "Missing Roman" }, new S.FontFamilyNumbering { Val = 1 }, new S.FontSize { Val = 11 }),
                new S.Font(new S.FontName { Val = "游ゴシック" }, new S.FontSize { Val = 11 }));
            styles.CellFormats!.Append(new S.CellFormat { FontId = 1, ApplyFont = true }, new S.CellFormat { FontId = 2, ApplyFont = true });
            var data = TestSupport.Sheet(workbook).GetFirstChild<S.SheetData>()!;
            data.RemoveAllChildren();
            data.Append(new S.Row(TestWorkbook.Text("A1", "iiii WWWW", 1), TestWorkbook.Text("B1", "iiii WWWW", 2)) { RowIndex = 1 });
        });
        using (var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default))
        using (var fonts = new SingleFontContext(Fonts()))
        {
            var sheet = new CoreExcelReader(fonts).Read(prepared, null).Sheets[0];
            Assert.Equal(fonts.Resolve("Noto Sans Mono").Identity, sheet.Cells[new(1, 1)].Style!.Font.Family);
            Assert.Equal(fonts.Resolve("Noto Sans JP").Identity, sheet.Cells[new(1, 2)].Style!.Font.Family);
        }

        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.ConvertAsync(input, output, new() { Fonts = Fonts() });
        using var pdf = TestSupport.Open(output.ToArray());
        var letters = pdf.GetPage(1).Letters;
        Assert.Contains(letters, letter => letter.FontName?.Contains("NotoSansMono", StringComparison.Ordinal) == true);
        Assert.Contains(letters, letter => letter.FontName?.Contains("NotoSansJP", StringComparison.Ordinal) == true);
        Assert.Equal(2, TestSupport.Fonts(pdf).Count());
    }

    [Fact]
    public void Missing_classified_fallback_uses_default_and_first_file_is_implicit_default()
    {
        using var fonts = new SingleFontContext(new FontOptions { Files = [TestSupport.MonoFont] });
        Assert.Equal("Noto Sans Mono", fonts.Resolve("Missing", 1).Typeface.FamilyName);
        Assert.Same(fonts.Face, fonts.Resolve("Missing", 2));
    }

    [Fact]
    public void Missing_glyphs_do_not_switch_to_another_supplied_font()
    {
        using var fonts = new SingleFontContext(Fonts());
        var result = new SkiaTextMeasurer(fonts).Layout("日本語", new("Noto Sans Mono", 11), 100, false);
        Assert.Equal(fonts.Resolve("Noto Sans Mono").Identity, result.Lines[0].Glyphs!.FontIdentity);
        Assert.Contains((ushort)0, result.Lines[0].Glyphs!.Glyphs);
        Assert.DoesNotContain((ushort)0, fonts.Resolve("Noto Sans JP").Typeface.GetGlyphs("日本語"));
    }

    [Fact]
    public void Failed_font_configuration_disposes_all_loaded_faces()
    {
        var counts = new Dictionary<string, double>();
        var previous = Rendering.ConversionMetrics.Observer;
        Rendering.ConversionMetrics.Observer = (name, value) => counts[name] = counts.GetValueOrDefault(name) + value;
        try
        {
            Assert.Throws<ArgumentException>(() => new SingleFontContext(new FontOptions
            {
                Files = [TestSupport.JapaneseFont, TestSupport.MonoFont],
                DefaultFamily = "Missing",
            }));
            Assert.Equal(2, counts["typefaceCreated"]);
            Assert.Equal(counts["typefaceCreated"], counts["typefaceDisposed"]);
        }
        finally
        {
            Rendering.ConversionMetrics.Observer = previous;
        }
    }

    [Fact]
    public async Task Conflicting_legacy_and_multiple_font_options_fail_without_output()
    {
        using var input = TestSupport.Workbook();
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.ConvertAsync(input, output, TestSupport.Options with { Fonts = Fonts() }));
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.ConvertAsync(input, output, new() { Fonts = Fonts(), FontCollectionIndex = 1 }));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("missing")]
    [InlineData("default")]
    [InlineData("serif")]
    [InlineData("sans")]
    [InlineData("alias")]
    [InlineData("blank-alias")]
    public void Invalid_configuration_fails_instead_of_silently_selecting_another_family(string problem)
    {
        var files = problem == "empty" ? Array.Empty<string>() : problem == "blank" ? [""] : new[] { TestSupport.JapaneseFont };
        var options = new FontOptions
        {
            Files = problem == "missing" ? ["missing-font.ttf"] : files,
            DefaultFamily = problem == "default" ? "Missing" : null,
            SerifFallback = problem == "serif" ? "Missing" : null,
            SansSerifFallback = problem == "sans" ? "Missing" : null,
        };
        if (problem == "alias") options.FamilyAliases.Add("Source", "Missing");
        if (problem == "blank-alias") options.FamilyAliases.Add("", "Noto Sans JP");
        if (problem == "missing")
        {
            Assert.Throws<FileNotFoundException>(() => new SingleFontContext(options));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new SingleFontContext(options));
        }
    }
}
