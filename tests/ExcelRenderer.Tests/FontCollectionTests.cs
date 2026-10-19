using ExcelRenderer.Core.Fonts.TrueType;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using ExcelRenderer.TestFixtures;
using SkiaSharp;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class FontCollectionTests
{
    [Theory]
    [InlineData("collection-distinct.ttc")]
    [InlineData("collection-v2.ttc")]
    public void Style_indices_and_scans_select_distinct_metrics_and_glyphs(string name)
    {
        var path = Fixture(name);
        var manager = new FontManager(new()
        {
            AllowSystemFonts = false, UseFontPack = false,
            Registrations = [new("Collection", path, path) { BoldCollectionIndex = 1 }],
        });
        var regular = manager.Resolve(new("Collection", 400, false));
        var bold = manager.Resolve(new("Collection", 700, false));
        Assert.Equal(0, regular.FontCollectionIndex);
        Assert.Equal(1, bold.FontCollectionIndex);
        Assert.NotEqual(regular.FaceId, bold.FaceId);
        using var resources = new ConversionFontResources();
        var first = resources.GetFace(regular);
        var second = resources.GetFace(bold);
        Assert.Equal(6, first.Measure([2], 10));
        Assert.Equal(8.5, second.Measure([2], 10));
        Assert.NotEqual(first.Typeface.GetGlyphs("B"), second.Typeface.GetGlyphs("B"));
        var direct = new FontManager(new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [path] });
        Assert.Equal(1, direct.Resolve(new("PDF Format Fixture", 700, false)).FontCollectionIndex);
        var scanned = new FontManager(new() { AllowSystemFonts = false, UseFontPack = false, FontDirectories = [Path.GetDirectoryName(path)!] });
        Assert.Equal(1, scanned.Resolve(new("PDF Format Fixture", 700, false)).FontCollectionIndex);
        using var sourceData = SKData.CreateCopy(File.ReadAllBytes(path));
        using var source = SKTypeface.FromData(sourceData, 1);
        using var original = new SKFont(source, 20);
        using var extracted = new SKFont(second.Typeface, 20);
        Assert.Equal(original.GetGlyphs("A B"), extracted.GetGlyphs("A B"));
        Assert.Equal(original.GetGlyphWidths([2, 3]), extracted.GetGlyphWidths([2, 3]));
        using var before = original.GetGlyphPath(2);
        using var after = extracted.GetGlyphPath(2);
        Assert.Equal(before!.ToSvgPathData(), after!.ToSvgPathData());
    }

    [Fact]
    public void File_memory_and_frozen_snapshots_share_only_the_selected_normalized_face()
    {
        var path = Fixture("collection-distinct.ttc");
        var source = File.ReadAllBytes(path);
        using var resources = new ConversionFontResources();
        var first = new ResolvedFont("Collection", 400, false, path);
        var second = first with { FontCollectionIndex = 1 };
        var fromFile = resources.GetFace(second);
        var fromMemory = resources.GetFace(second with { FontData = source });
        Assert.Same(fromFile, fromMemory);
        Assert.NotSame(resources.GetFace(first), fromFile);
        var frozen = resources.Freeze(second);
        Assert.Equal(1, frozen.FontCollectionIndex);
        using var nested = new ConversionFontResources();
        Assert.Equal(fromFile.Data, nested.GetFace(frozen).Data);
        Assert.Equal(8.5, nested.GetFace(frozen).Measure([2], 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Custom_resolver_keeps_the_nonzero_memory_index_and_uses_color_without_subsetting(int index)
    {
        var path = Fixture("collection-mixed.ttc");
        var face = new ResolvedFont("Custom Collection", 400, false, path)
        {
            FontData = File.ReadAllBytes(path), FontCollectionIndex = index,
        };
        var manager = new CustomManager(face);
        var measurer = new SkiaTextMeasurer(manager);
        var style = Model.CellStyle.Default with { Font = new("Custom Collection", 20) };
        var command = new Drawing.DrawTextCommand(1, new(10, 10, 100, 50), "A B", style)
        {
            TextLayout = measurer.Layout("A B", style.Font, 100, false),
        };
        var calls = 0;
        ConversionMetrics.Observer = (name, _) => { if (name == "pdfSubset.ms") calls++; };
        try
        {
            using var writer = new SkiaPdfDocumentWriter(manager, default);
            writer.Collect([command]);
            if (index == 1)
            {
                Assert.Throws<NotSupportedException>(writer.Prepare);
                return;
            }

            writer.Prepare();
            writer.Append(new(150, 100), new(0, "collection", 1, 1, 1, 150, 100), [command], [], _ => { });
            using var output = new MemoryStream();
            writer.Complete(output);
            Assert.Equal(index >= 3 ? 0 : 1, calls);
            var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "ManagedTrueType");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"collection-{index}.pdf"), output.ToArray());
            using var parsed = PdfTestFile.Open(new MemoryStream(output.ToArray()));
            Assert.Single(parsed.Pages);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Theory]
    [InlineData("cff.otf")]
    [InlineData("variable.ttf")]
    public async Task Unused_unsupported_registration_does_not_fail_pdf_and_other_formats_keep_working(string name)
    {
        var options = new FontOptions
        {
            AllowSystemFonts = false, UseFontPack = false, FallbackFamilies = ["PDF Format Fixture"],
            Registrations = [new("PDF Format Fixture", Fixture("static.ttf")), new("unused", Fixture(name))],
        };
        using var input = Workbook();
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Pdf, FontOptions = options }, new SingleStreamOutputSink(output));
        Assert.True(output.Length > 500);
        var manager = new FontManager(new()
        {
            AllowSystemFonts = false, UseFontPack = false, FallbackFamilies = ["unsupported"],
            Registrations = [new("unsupported", Fixture(name))],
        });
        using var png = new SKBitmap(100, 80);
        using var canvas = new SKCanvas(png);
        using var paint = new SKPaint();
        using var resources = new ConversionFontResources();
        var face = resources.GetFace(manager.Resolve(new("unsupported", 400, false)));
        using var font = new SKFont(face.Typeface, 20);
        canvas.DrawText("A", 5, 30, SKTextAlign.Left, font, paint);
        Assert.NotEqual(SKColors.Transparent, png.GetPixel(10, 20));
    }

    [Theory]
    [InlineData("collection-distinct.ttc", 1)]
    [InlineData("collection-v2.ttc", 1)]
    [InlineData("collection-mixed.ttc", 0)]
    public async Task Public_and_core_conversion_accept_selected_collection_faces(string name, int index)
    {
        var path = Fixture(name);
        using var input = Workbook();
        using var mainOutput = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            FontOptions = new()
            {
                AllowSystemFonts = false, UseFontPack = false, FallbackFamilies = ["PDF Format Fixture"],
                FontFiles = [path], FontFileCollectionIndex = index,
            },
        }, new SingleStreamOutputSink(mainOutput));
        input.Position = 0;
        using var coreOutput = new MemoryStream();
        var result = await Core.ExcelConverter.ConvertAsync(input, coreOutput, new() { FontFilePath = path, FontCollectionIndex = index });
        Assert.Equal(1, result.PageCount);
        using var mainPdf = PdfTestFile.Open(new MemoryStream(mainOutput.ToArray()));
        using var corePdf = PdfTestFile.Open(new MemoryStream(coreOutput.ToArray()));
        Assert.Contains("A", mainPdf.Pages[0].Text);
        Assert.Contains("A", corePdf.Pages[0].Text);
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "ManagedTrueType");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"{name}-{index}-main.pdf"), mainOutput.ToArray());
        File.WriteAllBytes(Path.Combine(directory, $"{name}-{index}-core.pdf"), coreOutput.ToArray());
    }

    [Theory]
    [InlineData("cff.otf")]
    [InlineData("variable.ttf")]
    public async Task Excluded_page_only_unsupported_face_is_not_prepared(string name)
    {
        using var input = Workbook();
        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
        {
            var workbook = document.WorkbookPart!;
            var styles = workbook.WorkbookStylesPart!.Stylesheet;
            styles.Fonts!.Append(new S.Font(new S.FontName { Val = "Unsupported" }, new S.FontSize { Val = 11 }));
            var format = (S.CellFormat)styles.CellFormats!.FirstChild!.CloneNode(true);
            format.FontId = 1;
            var styleIndex = (uint)styles.CellFormats.ChildElements.Count;
            styles.CellFormats.Append(format);
            var sheet = workbook.WorksheetParts.Single().Worksheet;
            var cell = SdkWorkbook.Text("A2", "A B");
            cell.StyleIndex = styleIndex;
            sheet.GetFirstChild<S.SheetData>()!.Append(new S.Row(cell) { RowIndex = 2 });
            sheet.Append(new S.RowBreaks(new S.Break { Id = 1, Min = 0, Max = 16383, ManualPageBreak = true }) { Count = 1, ManualBreakCount = 1 });
            workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("'data'!$A$1:$A$2") { Name = "_xlnm.Print_Area", LocalSheetId = 0 });
        }

        var fonts = new FontOptions
        {
            AllowSystemFonts = false, UseFontPack = false, FallbackFamilies = ["Noto Sans JP"],
            Registrations = [new("Noto Sans JP", Fixture("static.ttf")), new("Unsupported", Fixture(name))],
        };
        input.Position = 0;
        using var selected = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf, FontOptions = fonts, Selection = new() { Pages = [1] },
        }, new SingleStreamOutputSink(selected));
        Assert.Single(result.Pages);
        Assert.True(selected.Length > 500);
        input.Position = 0;
        using var all = new MemoryStream();
        var error = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input,
            new() { OutputFormat = OutputFormat.Pdf, FontOptions = fonts }, new SingleStreamOutputSink(all)));
        Assert.Empty(error.CompletedArtifacts);
        Assert.Equal(0, all.Length);
    }

    [Theory]
    [InlineData("cff.otf", OutputFormat.Png)]
    [InlineData("cff.otf", OutputFormat.Svg)]
    [InlineData("variable.ttf", OutputFormat.Png)]
    [InlineData("variable.ttf", OutputFormat.Svg)]
    public async Task Pdf_only_restrictions_do_not_remove_public_png_svg_support(string name, OutputFormat format)
    {
        using var input = Workbook();
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = format, Dpi = 24,
            FontOptions = new()
            {
                AllowSystemFonts = false, UseFontPack = false, FallbackFamilies = ["Noto Sans JP"],
                Registrations = [new("Noto Sans JP", Fixture(name))],
            },
        }, new SingleStreamOutputSink(output));
        Assert.Single(result.Artifacts);
        Assert.True(output.Length > 100);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", name);

    private static MemoryStream Workbook() => SdkWorkbook.Create(new(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "A B")) { RowIndex = 1 })), SdkWorkbook.Styles());

    private sealed class CustomManager(ResolvedFont face) : IFontManager
    {
        public ResolvedFont Resolve(FontRequest request) => face;
        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request) => [new(text, face)];
    }
}
