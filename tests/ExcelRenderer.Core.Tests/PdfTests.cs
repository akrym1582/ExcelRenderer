using S = DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;
using Xunit;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig;

namespace ExcelRenderer.Core.Tests;

public sealed class PdfTests
{
    [Fact]
    public async Task Japanese_text_has_embedded_font_and_ToUnicode_map()
    {
        var (result, bytes) = await TestSupport.Convert();
        Assert.Equal(1, result.PageCount);
        using var document = TestSupport.Open(bytes);
        var fonts = TestSupport.Fonts(document).ToArray(); Assert.NotEmpty(fonts);
        foreach (var font in fonts)
        {
            var embedded = TestSupport.Entry<StreamToken>(document, TestSupport.Descriptor(document, font), "FontFile2");
            Assert.True(embedded.Data.Length > 1000);
            var map = TestSupport.Entry<StreamToken>(document, font, "ToUnicode");
            var text = System.Text.Encoding.ASCII.GetString(map.Decode(DefaultFilterProvider.Instance).Span);
            Assert.Contains("beginbfrange", text);
            Assert.Contains("本語", document.GetPage(1).Text);
            Assert.Contains("672C", text, StringComparison.OrdinalIgnoreCase);
            var actualTexts = document.GetPage(1).Operations
                .OfType<UglyToad.PdfPig.Graphics.Operations.MarkedContent.BeginMarkedContentWithProperties>()
                .Where(operation => operation.Properties?.Data.ContainsKey("ActualText") == true)
                .Select(operation => TestSupport.PdfString(operation.Properties!.Data["ActualText"]));
            Assert.True(document.GetPage(1).Text.Contains("日本語", StringComparison.Ordinal) || actualTexts.Contains("日"));
        }
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "japanese.pdf"), bytes);
    }

    [Fact]
    public async Task Cell_fonts_are_normalized_but_size_color_and_underline_remain()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var styles = workbook.WorkbookStylesPart!.Stylesheet;
            styles.Fonts!.ReplaceChild(new S.Font(new S.Bold(), new S.Italic(), new S.Underline(), new S.FontSize { Val = 18 }, new S.Color { Rgb = "FFFF0000" }, new S.FontName { Val = "unavailable font" }), styles.Fonts.FirstChild!);
            styles.CellFormats!.GetFirstChild<S.CellFormat>()!.Alignment = new S.Alignment { TextRotation = 40 };
        });
        await TestSupport.WithSheet(input, (sheet, context) =>
        {
            var style = sheet.Cells[new(1, 1)].Style;
            Assert.Equal(SingleFontContext.Family, style.Font.Family);
            Assert.Equal(18, style.Font.Size); Assert.Equal(new ReportColor(255, 0, 0), style.Font.Color);
            Assert.True(style.Font.Underline);
        });
    }

    [Fact]
    public async Task Font_snapshot_is_read_once_even_if_file_changes()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".bin");
        File.Copy(TestSupport.JapaneseFont, path); var reads = 0;
        ConversionMetrics.Observer = (key, _) => { if (key == "fontSnapshotRead") { reads++; File.WriteAllText(path, "changed"); } };
        try
        {
            var (_, pdf) = await TestSupport.Convert(options: TestSupport.Options with { FontFilePath = path });
            Assert.Equal(1, reads); Assert.Contains("NotoSansJP", TestSupport.FontName(pdf));
        }
        finally { ConversionMetrics.Observer = null; File.Delete(path); }
    }

    [Fact]
    public async Task Sequential_and_parallel_font_requests_do_not_mix()
    {
        foreach (var font in new[] { TestSupport.JapaneseFont, TestSupport.MonoFont, TestSupport.JapaneseFont })
        {
            var (_, pdf) = await TestSupport.Convert(options: TestSupport.Options with { FontFilePath = font });
            Assert.Contains(font == TestSupport.MonoFont ? "NotoSansMono" : "NotoSansJP", TestSupport.FontName(pdf));
        }
        var tasks = Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            var (_, pdf) = await TestSupport.Convert(options: TestSupport.Options with { FontFilePath = i % 2 == 0 ? TestSupport.MonoFont : TestSupport.JapaneseFont });
            return (i, TestSupport.FontName(pdf));
        }));
        foreach (var (index, name) in await Task.WhenAll(tasks)) Assert.Contains(index % 2 == 0 ? "NotoSansMono" : "NotoSansJP", name);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Finalized_baseline_and_underline_follow_stored_geometry(int h, int v)
    {
        var horizontal = (HorizontalAlignment)h; var vertical = (VerticalAlignment)v;
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        {
            var measurer = new SkiaTextMeasurer(font);
            var style = CellStyle.Default with { Font = new(Size: 18, Underline: true), HorizontalAlignment = horizontal, VerticalAlignment = vertical };
            var layout = measurer.Layout("abc", style.Font, 100, false);
            var command = new Drawing.DrawTextCommand(1, new(20, 30, 100, 80), "abc", style) { TextLayout = layout };
            using var renderer = new CorePdfRenderer(font); renderer.Collect([command]); renderer.Prepare(); renderer.AppendPage(new(200, 200), [command]);
            using var output = new MemoryStream(); renderer.Complete(output);
            var probe = PdfContentProbe.Read(output.ToArray());
            Assert.Equal("abc", string.Concat(probe.Texts.Select(text => text.Value)));
            var text = probe.Texts[0];
            var line = layout.Lines.Single();
            var left = 20 + (horizontal == HorizontalAlignment.Center ? (100 - line.Width) / 2 : horizontal == HorizontalAlignment.Right ? 100 - line.Width : 0);
            var top = 30 + (vertical == VerticalAlignment.Center ? (80 - layout.Size.Height) / 2 : vertical == VerticalAlignment.Bottom ? 80 - layout.Size.Height : 0);
            TestSupport.Near(left, text.Origin.X); TestSupport.Near(top + line.Baseline, text.Origin.Y); TestSupport.Near(18, text.Size);
            var underline = Assert.Single(probe.Paints, p => p.Operation == "S");
            TestSupport.Near(left, underline.Points[0].X); TestSupport.Near(text.Origin.Y + 1, underline.Points[0].Y);
            TestSupport.Near(left + line.Width, underline.Points[1].X);
        }
    }

    [Fact]
    public async Task Rotation_is_ignored_and_shrink_reduces_the_PDF_font_size()
    {
        async Task<byte[]> Pdf(int rotation, bool shrink)
        {
            var (_, bytes) = await TestSupport.Convert(workbook =>
            {
                var sheet = TestSupport.Sheet(workbook);
                sheet.GetFirstChild<S.SheetData>()!.GetFirstChild<S.Row>()!.ReplaceChild(TestWorkbook.Text("A1", "abcdefghijklmnopqrstuv"), sheet.Descendants<S.Cell>().Single());
                sheet.InsertAt(new S.Columns(new S.Column { Min = 1, Max = 1, Width = 4.710625, CustomWidth = true }), 0);
                workbook.WorkbookStylesPart!.Stylesheet.Fonts!.GetFirstChild<S.Font>()!.FontSize = new S.FontSize { Val = 20 };
                workbook.WorkbookStylesPart.Stylesheet.CellFormats!.GetFirstChild<S.CellFormat>()!.Alignment = new S.Alignment { TextRotation = (uint)rotation, ShrinkToFit = shrink };
            }); return bytes;
        }
        var normal = PdfContentProbe.Read(await Pdf(0, false));
        var rotated = PdfContentProbe.Read(await Pdf(90, false));
        Assert.Equal(normal.Texts, rotated.Texts);
        var shrunk = PdfContentProbe.Read(await Pdf(255, true));
        Assert.NotEmpty(shrunk.Texts);
        Assert.All(shrunk.Texts, text => Assert.InRange(text.Size, 0.1, 19.9));
        Assert.True(shrunk.ClipCount > 0);
    }

    [Fact]
    public async Task Wrap_newlines_surrogates_and_merged_cells_are_supported()
    {
        var (result, bytes) = await TestSupport.Convert(workbook =>
        {
            var sheet = TestSupport.Sheet(workbook);
            sheet.Descendants<S.Text>().Single().Text = "日本語\nabc😀";
            sheet.InsertAt(new S.Columns(new S.Column { Min = 1, Max = 2, Width = 4.710625, CustomWidth = true }), 0);
            var styles = workbook.WorkbookStylesPart!.Stylesheet;
            styles.Fills!.ReplaceChild(new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = "FFFFFF00" }) { PatternType = S.PatternValues.Solid }), styles.Fills.FirstChild!);
            styles.CellFormats!.GetFirstChild<S.CellFormat>()!.Alignment = new S.Alignment { WrapText = true };
            TestSupport.MergeDoubleBorder(workbook);
        });
        Assert.Equal(1, result.PageCount); Assert.True(PdfContentProbe.Read(bytes).Texts.Count >= 2);
    }

    [Fact]
    public async Task One_active_page_one_save_and_native_cleanup_are_observed()
    {
        var active = 0; var max = 0; var saves = 0; var created = 0; var disposed = 0;
        ConversionMetrics.Observer = (key, value) =>
        {
            if (key == "pagePayloadActive") { active = (int)value; max = Math.Max(max, active); }
            if (key == "pdfSave") saves++;
            if (key == "typefaceCreated") created++;
            if (key == "typefaceDisposed") disposed++;
        };
        try
        {
            var (result, _) = await TestSupport.Convert(workbook =>
            {
                var data = TestSupport.Sheet(workbook).GetFirstChild<S.SheetData>()!;
                data.RemoveAllChildren();
                for (var i = 1; i <= 200; i++) data.Append(new S.Row(TestWorkbook.Text("A" + i, "行 " + i)) { RowIndex = (uint)i });
            });
            Assert.True(result.PageCount > 1); Assert.Equal(1, max); Assert.Equal(0, active);
            Assert.Equal(1, saves); Assert.Equal(1, created); Assert.Equal(created, disposed);
        }
        finally { ConversionMetrics.Observer = null; }
    }

    [Theory]
    [InlineData("01-japanese.xlsx", 2)]
    [InlineData("02-image.xlsx", 2)]
    [InlineData("03-wrapped-text.xlsx", 1)]
    [InlineData("04-text-decoration.xlsx", 1)]
    [InlineData("05-borders.xlsx", 2)]
    [InlineData("06-layout-and-pagination.xlsx", 4)]
    [InlineData("07-multiple-sheets.xlsx", 2)]
    [InlineData("08-print-scaling.xlsx", 2)]
    [InlineData("09-cell-border.xlsx", 1)]
    public async Task Core_owned_workbooks_keep_expected_page_counts_and_readable_PDF(string name, int expectedPages)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Baselines", "Inputs", name)); using var output = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        using var document = TestSupport.Open(output.ToArray());
        Assert.Equal(expectedPages, result.PageCount);
        Assert.Equal(expectedPages, document.NumberOfPages);
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name.Replace(".xlsx", ".pdf")), output.ToArray());
    }
}
