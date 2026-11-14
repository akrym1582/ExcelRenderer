using ExcelRenderer.Excel;
using ExcelRenderer.Mapping;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using SkiaSharp;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class MappingImageTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Png, OutputFormat.Pdf)]
    [InlineData(SKEncodedImageFormat.Jpeg, OutputFormat.Png)]
    [InlineData(SKEncodedImageFormat.Png, OutputFormat.Svg)]
    public async Task Bound_images_and_colors_survive_reading_and_rendering(SKEncodedImageFormat encoding, OutputFormat format)
    {
        using var bitmap = new SKBitmap(40, 20);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(encoding, 100);
        using var template = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Text("A1", "**//internal")) { RowIndex = 1 },
            new S.Row(SdkWorkbook.Text("A2", "**@image $.Logo width=120 offset-x=8 offset-y=-4"), SdkWorkbook.Text("B2", "**$.Label | font-color($.Color)")) { RowIndex = 2 })), SdkWorkbook.Styles());
        using var mapped = new MemoryStream();
        ExcelTemplateMapper.Map(template, mapped, new { Logo = bytes.ToArray(), Label = "BOUND", Color = "#FF0000" });
        mapped.Position = 0;
        var sheet = new ExcelReader().Read(mapped).Sheets.Single();
        var actual = Assert.Single(sheet.Images!);
        Assert.Equal(90, actual.Width);
        Assert.Equal(45, actual.Height);
        Assert.Equal(6, actual.OffsetX);
        Assert.Equal(-3, actual.OffsetY);
        Assert.Equal(new CellAddress(2, 1), actual.Anchor);
        Assert.Equal(new ReportColor(255, 0, 0), sheet.Cells[new(2, 2)].Style.Font.Color);
        mapped.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(mapped, new RenderRequest
        {
            OutputFormat = format,
            FontOptions = new() { AllowSystemFonts = false },
            ReadOptions = new() { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 7 } },
        }, new SingleStreamOutputSink(output));
        Assert.Single(result.Artifacts);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Image"));
        Assert.True(output.Length > 100);
        if (format == OutputFormat.Png)
        {
            using var rendered = SKBitmap.Decode(output.ToArray());
            Assert.Contains(rendered.Pixels, pixel => pixel.Red > 200 && pixel.Green < 40 && pixel.Blue < 40);
        }
        else if (format == OutputFormat.Svg)
        {
            Assert.Contains("image", System.Text.Encoding.UTF8.GetString(output.ToArray()));
        }
        else
        {
            output.Position = 0;
            using var pdf = PdfTestFile.Open(output);
            Assert.Contains("BOUND", pdf.Pages[0].Text);
            Assert.NotNull(pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject"));
        }
    }
}
