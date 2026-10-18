using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class CellArgbTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "SampleInputs", "10-cell-argb.xlsx");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_makes_cell_styles_and_inline_or_shared_rich_text_opaque(bool shared)
    {
        using var input = new MemoryStream();
        input.Write(File.ReadAllBytes(FixturePath));
        input.Position = 0;
        if (shared)
        {
            using var document = SpreadsheetDocument.Open(input, true);
            var workbook = document.WorkbookPart!;
            var strings = workbook.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new S.SharedStringTable();
            foreach (var cell in workbook.WorksheetParts.Single().Worksheet.Descendants<S.Cell>()
                .Where(cell => cell.CellReference!.Value!.StartsWith("E") && cell.InlineString!.Elements<S.Run>().Any()))
            {
                var index = strings.SharedStringTable.ChildElements.Count;
                strings.SharedStringTable.Append(new S.SharedStringItem(cell.InlineString!.ChildElements.Select(child => child.CloneNode(true))));
                cell.InlineString = null;
                cell.DataType = S.CellValues.SharedString;
                cell.CellValue = new S.CellValue(index.ToString());
            }
        }
        input.Position = 0;
        var sheet = Assert.Single(new ExcelReader().Read(input).Sheets);
        for (var row = 2; row <= 6; row++)
        {
            Assert.Equal(new ReportColor(0, 0, 0), sheet.Cells[new(row, 2)].Style.Font.Color);
            Assert.Equal(new ReportColor(51, 102, 153), sheet.Cells[new(row, 3)].Style.Background);
            var border = sheet.Cells[new(row, 4)].Style.Border!;
            Assert.All(new[] { border.Left, border.Top, border.Right, border.Bottom },
                side => Assert.Equal(new ReportColor(204, 51, 0), side!.Color));
            Assert.Equal(new ReportColor(0, 0, 0), Assert.Single(sheet.Cells[new(row, 5)].RichTextRuns!).Font.Color);
        }
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    public async Task Saved_output_matches_an_explicitly_opaque_workbook(OutputFormat format)
    {
        using var original = new MemoryStream(File.ReadAllBytes(FixturePath));
        using var opaque = new MemoryStream();
        original.CopyTo(opaque);
        using (var document = SpreadsheetDocument.Open(opaque, true))
        {
            var workbook = document.WorkbookPart!;
            // Change only source alpha bytes, including inline rich-text colors.
            foreach (var element in workbook.WorkbookStylesPart!.Stylesheet.Descendants()
                .Concat(workbook.WorksheetParts.Single().Worksheet.Descendants()))
            {
                var rgb = element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "rgb").Value;
                if (rgb?.Length == 8)
                    element.SetAttribute(new DocumentFormat.OpenXml.OpenXmlAttribute("rgb", string.Empty, "FF" + rgb[2..]));
            }
        }
        async Task<byte[]> Render(Stream input)
        {
            input.Position = 0;
            using var output = new MemoryStream();
            var result = await ExcelConverter.RenderAsync(input, new RenderRequest
            {
                OutputFormat = format,
                Dpi = 144,
                FontOptions = new() { AllowSystemFonts = false },
            }, new SingleStreamOutputSink(output));
            Assert.Single(result.Pages);
            return output.ToArray();
        }
        var actual = await Render(original);
        var expected = await Render(opaque);
        if (format == OutputFormat.Png)
        {
            using var image = SKBitmap.Decode(actual);
            using var reference = SKBitmap.Decode(expected);
            Assert.Equal(reference.Pixels, image.Pixels);
            // Independent visibility checks prevent an equally blank reference passing.
            var sample = SampleOutputTestSupport.ReadAndLayout("10-cell-argb.xlsx");
            foreach (var cell in sample.Layout.Pages.Single().Cells.Where(cell => cell.SourceAddress?.Row >= 2))
            {
                var bounds = cell.Bounds;
                var x = (int)((bounds.X + bounds.Width / 2) * 2);
                var y = (int)((bounds.Y + bounds.Height / 2) * 2);
                if (cell.SourceAddress!.Value.Column == 3)
                    Assert.Equal(new SKColor(51, 102, 153), image.GetPixel(x, y));
                if (cell.SourceAddress.Value.Column == 4)
                    Assert.Equal(new SKColor(204, 51, 0), image.GetPixel(x, (int)(bounds.Y * 2)));
                if (cell.SourceAddress.Value.Column is 2 or 5)
                    Assert.Contains(Enumerable.Range((int)(bounds.X * 2) + 2, (int)(bounds.Width * 2) - 4)
                        .SelectMany(px => Enumerable.Range((int)(bounds.Y * 2) + 2, (int)(bounds.Height * 2) - 4)
                            .Select(py => image.GetPixel(px, py))), pixel => pixel.Red < 64 && pixel.Green < 64 && pixel.Blue < 64);
            }
        }
        else
        {
            Assert.Equal(OutputFixture.Content(expected), OutputFixture.Content(actual));
            using var pdf = PdfReader.Open(new MemoryStream(actual), PdfDocumentOpenMode.Import);
            var states = pdf.Pages[0].Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/ExtGState");
            if (states is not null)
                foreach (var key in states.Elements.Keys)
                    foreach (var channel in new[] { "/ca", "/CA" })
                        if (states.Elements.GetDictionary(key)!.Elements.ContainsKey(channel))
                            Assert.Equal(1, states.Elements.GetDictionary(key)!.Elements.GetReal(channel));
            Assert.NotEmpty(PdfContentProbe.Read(actual).Texts);
            Assert.NotEmpty(PdfContentProbe.Read(actual).Paints);
        }
    }

    [Fact]
    public void Image_alpha_remains_in_PNG_pixels_and_PDF_soft_mask()
    {
        using var source = new SKBitmap(3, 3);
        source.Erase(new SKColor(255, 0, 0, 128));
        using var sourceImage = SKImage.FromBitmap(source);
        using var data = sourceImage.Encode(SKEncodedImageFormat.Png, 100);
        DrawCommand[] commands = [new DrawImageCommand(1, new(0, 0, 12, 12), data.ToArray())];
        using var png = new MemoryStream();
        new PngRenderer().RenderPage(commands, new PageSettings(12, 12), png, 72);
        using var image = SKBitmap.Decode(png.ToArray());
        Assert.Equal(new SKColor(255, 127, 127), image.GetPixel(6, 6));
        using var output = new MemoryStream();
        new PdfSharpRenderer().Render(commands, new PageSettings(12, 12), output);
        using var pdf = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import);
        var objects = pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        Assert.Contains(objects.Elements.Keys, key => objects.Elements.GetDictionary(key)!.Elements.ContainsKey("/SMask"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void Drawing_model_keeps_PNG_transparency_and_PDF_semitransparency(byte alpha)
    {
        var color = new ReportColor(0, 0, 0, alpha);
        var shape = new ReportShape(new(1, 1), 0, 0, 10, 10, ShapeKind.Rectangle, new(color, null), null, 0, 0);
        DrawCommand[] commands =
        [
            new FillRectangleCommand(1, new(0, 0, 10, 10), color),
            new DrawShapeCommand(1, new(10, 0, 10, 10), shape),
            new DrawLineCommand(1, 22, 5, 28, 5, new BorderSide(2, color)),
            new DrawTextCommand(1, new(30, 0, 30, 20), "ABC", CellStyle.Default with { Font = new("Noto Sans JP", 11, Color: color) }),
        ];
        using var output = new MemoryStream();
        new PngRenderer().RenderPage(commands, new PageSettings(64, 24), output, 72);
        using var image = SKBitmap.Decode(output.ToArray());
        foreach (var x in new[] { 5, 15, 25 })
            Assert.Equal(new SKColor((byte)(255 - alpha), (byte)(255 - alpha), (byte)(255 - alpha)), image.GetPixel(x, 5));
        using var pdfOutput = new MemoryStream();
        new PdfSharpRenderer().Render(commands, new PageSettings(64, 24), pdfOutput);
        using var pdf = PdfReader.Open(new MemoryStream(pdfOutput.ToArray()), PdfDocumentOpenMode.Import);
        var states = pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/ExtGState");
        if (alpha == 128)
        {
            Assert.NotNull(states);
            Assert.Contains(states.Elements.Keys, key => Math.Abs(states.Elements.GetDictionary(key)!.Elements.GetReal("/ca") - alpha / 255d) < 0.01);
            Assert.Contains(states.Elements.Keys, key => Math.Abs(states.Elements.GetDictionary(key)!.Elements.GetReal("/CA") - alpha / 255d) < 0.01);
        }
    }
}
