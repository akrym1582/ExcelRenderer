using ClosedXML.Excel;
using ExcelRenderer.Slim.Fonts;
using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;
using ExcelRenderer.Slim.Pdf;
using ExcelRenderer.Slim.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Xunit;

namespace ExcelRenderer.Slim.Tests;

public sealed class PdfTests
{
    [Fact]
    public async Task Japanese_text_has_embedded_font_and_ToUnicode_map()
    {
        var (result, bytes) = await TestSupport.Convert();
        Assert.Equal(1, result.PageCount);
        using var document = TestSupport.Open(bytes);
        var fonts = TestSupport.Fonts(document.Pages[0]).ToArray(); Assert.NotEmpty(fonts);
        foreach (var font in fonts)
        {
            var embedded = TestSupport.Descriptor(font).Elements.GetDictionary("/FontFile2")!;
            Assert.NotNull(embedded.Stream); Assert.True(embedded.Stream.Value.Length > 1000);
            var map = font.Elements.GetDictionary("/ToUnicode")!;
            var text = System.Text.Encoding.ASCII.GetString(map.Stream.UnfilteredValue);
            Assert.Contains("beginbfrange", text);
            Assert.Contains("65E5", text, StringComparison.OrdinalIgnoreCase);
        }
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "japanese.pdf"), bytes);
    }

    [Fact]
    public async Task Cell_fonts_are_normalized_but_size_color_and_underline_remain()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var cell = workbook.Worksheet(1).Cell(1, 1);
            cell.Style.Font.FontName = "unavailable font"; cell.Style.Font.Bold = true; cell.Style.Font.Italic = true;
            cell.Style.Font.FontSize = 18; cell.Style.Font.FontColor = XLColor.Red;
            cell.Style.Font.Underline = XLFontUnderlineValues.Single; cell.Style.Alignment.TextRotation = 40;
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
        GlobalFontSettings.ResetFontManagement(); GlobalFontSettings.FontResolver = font.Resolver;
        try
        {
            var measurer = new PdfSharpTextMeasurer(font);
            var style = CellStyle.Default with { Font = new(Size: 18, Underline: true), HorizontalAlignment = horizontal, VerticalAlignment = vertical };
            var layout = measurer.Layout("abc", style.Font, 100, false);
            var command = new Drawing.DrawTextCommand(1, new(20, 30, 100, 80), "abc", style) { TextLayout = layout };
            using var document = new PdfDocument(); new PdfSharpRenderer(font).AppendPage(document, new(200, 200), [command]);
            using var output = new MemoryStream(); document.Save(output, false);
            var probe = PdfContentProbe.Read(output.ToArray()); var text = Assert.Single(probe.Texts);
            var line = layout.Lines.Single();
            var left = 20 + (horizontal == HorizontalAlignment.Center ? (100 - line.Width) / 2 : horizontal == HorizontalAlignment.Right ? 100 - line.Width : 0);
            var top = 30 + (vertical == VerticalAlignment.Center ? (80 - layout.Size.Height) / 2 : vertical == VerticalAlignment.Bottom ? 80 - layout.Size.Height : 0);
            TestSupport.Near(left, text.Origin.X); TestSupport.Near(top + line.Baseline, text.Origin.Y); TestSupport.Near(18, text.Size);
            var underline = Assert.Single(probe.Paints, p => p.Operation == "S");
            TestSupport.Near(left, underline.Points[0].X); TestSupport.Near(text.Origin.Y + 1, underline.Points[0].Y);
            TestSupport.Near(left + line.Width, underline.Points[1].X);
        }
        finally { GlobalFontSettings.ResetFontManagement(); }
    }

    [Fact]
    public async Task Rotation_is_ignored_and_shrink_reduces_the_PDF_font_size()
    {
        async Task<byte[]> Pdf(int rotation, bool shrink)
        {
            var (_, bytes) = await TestSupport.Convert(workbook =>
            {
                var sheet = workbook.Worksheet(1); sheet.Cell(1, 1).Value = "abcdefghijklmnopqrstuv";
                sheet.Column(1).Width = 4; sheet.Cell(1, 1).Style.Font.FontSize = 20;
                sheet.Cell(1, 1).Style.Alignment.TextRotation = rotation;
                sheet.Cell(1, 1).Style.Alignment.ShrinkToFit = shrink;
            }); return bytes;
        }
        var normal = PdfContentProbe.Read(await Pdf(0, false));
        var rotated = PdfContentProbe.Read(await Pdf(90, false));
        Assert.Equal(normal.Texts, rotated.Texts);
        var shrunk = PdfContentProbe.Read(await Pdf(255, true));
        Assert.InRange(shrunk.Texts.Single().Size, 0.1, 19.9);
        Assert.True(shrunk.AppliedClips.Count >= 2);
    }

    [Fact]
    public async Task Wrap_newlines_surrogates_and_merged_cells_are_supported()
    {
        var (result, bytes) = await TestSupport.Convert(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Cell(1, 1).Value = "日本語\nabc😀"; sheet.Range("A1:B2").Merge();
            sheet.Cell(1, 1).Style.Alignment.WrapText = true; sheet.Column(1).Width = 4; sheet.Column(2).Width = 4;
            sheet.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.Yellow;
            sheet.Range("A1:B2").Style.Border.OutsideBorder = XLBorderStyleValues.Double;
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
                var sheet = workbook.Worksheet(1);
                for (var i = 1; i <= 200; i++) sheet.Cell(i, 1).Value = "行 " + i;
            });
            Assert.True(result.PageCount > 1); Assert.Equal(1, max); Assert.Equal(0, active);
            Assert.Equal(1, saves); Assert.Equal(1, created); Assert.Equal(created, disposed);
        }
        finally { ConversionMetrics.Observer = null; }
    }

    [Theory]
    [InlineData("01-japanese.xlsx")]
    [InlineData("02-image.xlsx")]
    [InlineData("03-wrapped-text.xlsx")]
    [InlineData("04-text-decoration.xlsx")]
    [InlineData("05-borders.xlsx")]
    [InlineData("06-layout-and-pagination.xlsx")]
    [InlineData("07-multiple-sheets.xlsx")]
    [InlineData("08-print-scaling.xlsx")]
    [InlineData("09-cell-border.xlsx")]
    public async Task Existing_samples_produce_readable_PDF(string name)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SampleInputs", name)); using var output = new MemoryStream();
        var result = await SlimExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        using var document = TestSupport.Open(output.ToArray()); Assert.Equal(result.PageCount, document.PageCount); Assert.True(result.PageCount > 0);
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name.Replace(".xlsx", ".pdf")), output.ToArray());
    }
}
