using ClosedXML.Excel;
using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class LayoutDiagnosticTests
{
    [Fact]
    public async Task LayoutWarningsAreOptInAndCountOncePerPage()
    {
        using var input = CreateWorkbook();
        using var output = new MemoryStream();
        var defaults = await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Pdf }, new SingleStreamOutputSink(output));
        Assert.DoesNotContain(defaults.Diagnostics, d => d.Code is "CellTextClipped" or "CellTextTooSmall");
        input.Position = 0;
        using var warnedOutput = new MemoryStream();
        var warned = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            DiagnosticOptions = new() { EnableLayoutWarnings = true },
        }, new SingleStreamOutputSink(warnedOutput));
        var clipped = Assert.Single(warned.Diagnostics, d => d.Code == "CellTextClipped");
        Assert.Equal("A1", clipped.CellRange);
        Assert.Equal(1, clipped.OccurrenceCount);
        Assert.Equal(1, clipped.SourcePageNumber);
        var small = Assert.Single(warned.Diagnostics, d => d.Code == "CellTextTooSmall");
        Assert.Equal("A2", small.CellRange);
        Assert.DoesNotContain(warned.Diagnostics, d => d.Message.Contains("sensitive"));
    }

    [Fact]
    public async Task SuppressionStillFailsBeforeSinkIsOpened()
    {
        using var input = CreateWorkbook();
        using var output = new MemoryStream();
        output.Write([1, 2, 3]);
        var error = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            DiagnosticOptions = new()
            {
                EnableLayoutWarnings = true,
                TreatAsErrors = ["CellTextClipped"],
                SuppressedCodes = ["CellTextClipped"],
                MaxDiagnostics = 1,
            },
        }, new SingleStreamOutputSink(output)));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Fact]
    public async Task MarkdownHasNoLayoutWarningsAndConditionalFormattingIsOptIn()
    {
        using var input = CreateWorkbook();
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Markdown,
            DiagnosticOptions = new() { EnableLayoutWarnings = true, DetectUnsupportedFeatures = true },
        }, new SingleStreamOutputSink(output));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "CellTextClipped" or "CellTextTooSmall");
        Assert.Contains(result.Diagnostics, d => d.Code == "UnsupportedConditionalFormatting" && d.ObjectId == "conditional-format-1");
    }

    [Fact]
    public async Task UnselectedSheetsDoNotProduceNewWarnings()
    {
        using var input = CreateWorkbook();
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            Selection = new() { SheetNames = ["other"] },
            DiagnosticOptions = new() { EnableLayoutWarnings = true, DetectUnsupportedFeatures = true },
        }, new SingleStreamOutputSink(output));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "CellTextClipped" or "CellTextTooSmall" or "UnsupportedConditionalFormatting");
    }

    [Fact]
    public async Task FinalScaleRichRunsShrinkAndRotationProduceSmallTextWarnings()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("sizes");
        sheet.PageSetup.Scale = 50;
        sheet.PageSetup.PrintAreas.Add("A1:A5");
        sheet.Column(1).Width = 12;
        sheet.Cell(1, 1).Value = "scaled";
        sheet.Cell(1, 1).Style.Font.FontSize = 10;
        sheet.Cell(2, 1).CreateRichText().AddText("BIG").SetFontSize(20);
        sheet.Cell(2, 1).GetRichText().AddText("small").SetFontSize(8);
        sheet.Cell(3, 1).Value = "rotated";
        sheet.Cell(3, 1).Style.Font.FontSize = 8;
        sheet.Cell(3, 1).Style.Alignment.TextRotation = 45;
        sheet.Cell(4, 1).Value = "a long string that must shrink into a narrow cell";
        sheet.Cell(4, 1).Style.Font.FontSize = 20;
        sheet.Cell(4, 1).Style.Alignment.ShrinkToFit = true;
        sheet.Cell(5, 1).Value = "hidden";
        sheet.Cell(5, 1).Style.Font.FontSize = 4;
        sheet.Row(5).Hide();
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            DiagnosticOptions = new() { EnableLayoutWarnings = true },
        }, new SingleStreamOutputSink(output));
        Assert.Equal(new[] { "A1", "A2", "A3", "A4" }, result.Diagnostics.Where(d => d.Code == "CellTextTooSmall").Select(d => d.CellRange).Order());
        Assert.All(result.Diagnostics.Where(d => d.Code == "CellTextTooSmall"), diagnostic => Assert.Equal(1, diagnostic.OccurrenceCount));
    }

    private static MemoryStream CreateWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("diagnostics");
        sheet.Cell(1, 1).Value = "sensitive\nsecond\nthird";
        sheet.Cell(1, 1).Style.Alignment.WrapText = true;
        sheet.Row(1).Height = 10;
        sheet.Cell(2, 1).Value = "tiny";
        sheet.Cell(2, 1).Style.Font.FontSize = 4;
        sheet.Row(2).Height = 20;
        sheet.Cell(3, 1).Value = "hidden";
        sheet.Cell(3, 1).Style.Font.FontSize = 4;
        sheet.Row(3).Hide();
        sheet.Range("A1:A2").AddConditionalFormat().WhenNotBlank().Fill.SetBackgroundColor(XLColor.Red);
        workbook.AddWorksheet("other").Cell(1, 1).Value = "normal";
        var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        return input;
    }
}
