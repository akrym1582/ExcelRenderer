using System.IO.Compression;
using ClosedXML.Excel;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using Xunit;
using SkiaSharp;
using Svg.Skia;

namespace ExcelRenderer.Tests;

public sealed class RichTextAcceptanceTests
{
    [Theory]
    [InlineData(HorizontalAlignment.Left, VerticalAlignment.Top)]
    [InlineData(HorizontalAlignment.Center, VerticalAlignment.Center)]
    [InlineData(HorizontalAlignment.Right, VerticalAlignment.Bottom)]
    public void MergedShrunkMixedRunsKeepSizeRatiosAndAlignment(HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        var font = new FontStyle(Size: 20, Color: new(255, 0, 0));
        var style = new CellStyle(font, HorizontalAlignment: horizontal, VerticalAlignment: vertical, ShrinkToFit: true);
        var cell = new ReportCell("ABCDEFGHIJKL", style, ColumnSpan: 2)
        {
            RichTextRuns = [new("ABCDEF", font), new("GHIJKL", font with { Size = 40, Color = new(0, 0, 255) })],
        };
        var sheet = new ReportSheet("merged", new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = cell },
            new Dictionary<int, ColumnDefinition> { [1] = new(25), [2] = new(25) },
            new Dictionary<int, RowDefinition> { [1] = new(50) }, [new(new(1, 1), new(1, 2))], new(200, 200));
        var rendered = Assert.Single(new ReportLayoutEngine(new PdfSharpTextMeasurer(new OutputFixture.FixedManager())).Layout(sheet).Pages[0].Cells);
        var runs = rendered.TextLayout!.Lines.SelectMany(line => line.Runs).ToArray();
        Assert.Equal(2, runs.Length);
        Assert.True(runs[0].Font!.Size < 20);
        Assert.Equal(runs[0].Font!.Size * 2, runs[1].Font!.Size, 8);
        Assert.Equal("ABCDEFGHIJKL", string.Concat(runs.Select(run => run.Run.SourceText)));
        Assert.Equal(horizontal, rendered.Cell.Style.HorizontalAlignment);
        Assert.Equal(vertical, rendered.Cell.Style.VerticalAlignment);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void XmlCarriageReturnAndLineFeedAcrossRunsNormalizeTogether(bool shared)
    {
        using var workbook = new XLWorkbook();
        var source = workbook.AddWorksheet("newlines").Cell(1, 1);
        source.ShareString = shared;
        source.CreateRichText().AddText("BEFORECR").SetFontColor(XLColor.Red);
        source.GetRichText().AddText("AFTERLF").SetFontColor(XLColor.Blue);
        source.GetRichText().AddText("tail").SetFontColor(XLColor.Red);
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        using (var archive = new ZipArchive(input, ZipArchiveMode.Update, true))
        {
            var entry = archive.GetEntry(shared ? "xl/sharedStrings.xml" : "xl/worksheets/sheet1.xml")!;
            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
            var path = entry.FullName;
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(text.Replace("BEFORECR", "A&#xD;").Replace("AFTERLF", "&#xA;"));
        }

        input.Position = 0;
        var cell = new ExcelReader().Read(input).Sheets[0].Cells[new(1, 1)];
        Assert.Equal("A\ntail", cell.Text);
        var run = Assert.Single(cell.RichTextRuns!);
        Assert.Equal(cell.Text, run.Text);
        Assert.Equal(new ReportColor(255, 0, 0), run.Font.Color);
    }

    [Fact]
    public void ThemeTintAndInheritedRunAttributesAreEffective()
    {
        using var workbook = new XLWorkbook();
        workbook.Theme.Accent1 = XLColor.Red;
        var source = workbook.AddWorksheet("theme").Cell(1, 1);
        source.Style.Font.FontName = "Noto Sans JP";
        source.Style.Font.FontSize = 22;
        source.Style.Font.Bold = true;
        source.CreateRichText().AddText("inherited").SetFontColor(XLColor.FromTheme(XLThemeColor.Accent1, .5));
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        var cell = new ExcelReader().Read(input).Sheets[0].Cells[new(1, 1)];
        var run = Assert.Single(cell.RichTextRuns!);
        Assert.Equal(22, run.Font.Size);
        Assert.True(run.Font.Bold);
        Assert.Equal("Noto Sans JP", run.Font.Family);
        Assert.Equal(new ReportColor(255, 128, 128), run.Font.Color);
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    public async Task RangeTrimAndMergedCellRetainRichTextInAllOutputFormats(OutputFormat format)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("rich");
        sheet.Range("A1:B2").Merge();
        sheet.Cell(1, 1).CreateRichText().AddText("Red").SetFontColor(XLColor.Red).SetFontSize(14);
        sheet.Cell(1, 1).GetRichText().AddText("Blue").SetFontColor(XLColor.Blue).SetFontSize(20).SetUnderline();
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = format,
            Selection = new() { Ranges = [new("rich", new(new(1, 1), new(3, 3)))] },
            Trim = new() { Enabled = true },
        }, new SingleStreamOutputSink(output));
        Assert.Single(result.Artifacts);
        if (format == OutputFormat.Pdf)
        {
            var texts = PdfContentProbe.Read(output.ToArray()).Texts;
            Assert.Contains(texts, text => text.Size == 14);
            Assert.Contains(texts, text => text.Size == 20);
            return;
        }

        using var bitmap = format == OutputFormat.Png ? SKBitmap.Decode(output.ToArray()) : RasterSvg(output.ToArray());
        Assert.Contains(bitmap.Pixels, pixel => pixel.Red > 200 && pixel.Blue < 80 && pixel.Green < 80);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Blue > 200 && pixel.Red < 80 && pixel.Green < 80);

        static SKBitmap RasterSvg(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var svg = new SKSvg();
            var picture = svg.Load(stream)!;
            var bitmap = new SKBitmap((int)Math.Ceiling(picture.CullRect.Width), (int)Math.Ceiling(picture.CullRect.Height));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.DrawPicture(picture);
            return bitmap;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(255)]
    public async Task UnsupportedDecorationAndRotationPreserveTextAndReportOnce(int rotation)
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("rich").Cell(1, 1);
        cell.Style.Alignment.TextRotation = rotation;
        cell.CreateRichText().AddText("preserved").SetStrikethrough();
        cell.GetRichText().AddText(" text").SetFontSize(20);
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        var model = new ExcelReader().Read(input).Sheets[0].Cells[new(1, 1)];
        Assert.Equal("preserved text", model.Text);
        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            DiagnosticOptions = new() { DetectUnsupportedFeatures = true },
        }, new SingleStreamOutputSink(output));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "UnsupportedRichTextFormatting");
        Assert.Equal(1, diagnostic.OccurrenceCount);
        Assert.Equal("A1", diagnostic.CellRange);
    }
}
