using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Markdown;
using ExcelRenderer.TestFixtures;
using UglyToad.PdfPig;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class MultiSheetExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExcelRenderer.Sheets", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("ps")]
    [InlineData("markdown")]
    public async Task Format_specific_exports_select_multiple_sheets(string format)
    {
        var input = CreateWorkbook();
        var output = Path.Combine(_directory, "output");
        string[] names = ["Third", "First", "Third"];
        await ConvertAsync(format, input, output, names);
        if (format is "image" or "svg")
        {
            var files = Directory.GetFiles(output);
            Assert.Equal(2, files.Length);
            Assert.Contains(files, name => Path.GetFileName(name).Contains("Third", StringComparison.Ordinal));
            Assert.Contains(files, name => Path.GetFileName(name).Contains("First", StringComparison.Ordinal));
        }
        else if (format == "pdf")
        {
            using var pdf = PdfDocument.Open(output);
            Assert.Equal(2, pdf.NumberOfPages);
            Assert.Contains("ThirdValue", pdf.GetPage(1).Text);
            Assert.Contains("FirstValue", pdf.GetPage(2).Text);
        }
        else
        {
            var text = await File.ReadAllTextAsync(output);
            Assert.DoesNotContain("SecondValue", text);
            if (format == "markdown")
            {
                Assert.True(text.IndexOf("ThirdValue", StringComparison.Ordinal) < text.IndexOf("FirstValue", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains("%%Pages: 2", text);
            }
        }
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("ps")]
    [InlineData("markdown")]
    public async Task Invalid_or_conflicting_names_preserve_destination(string format)
    {
        var input = CreateWorkbook();
        var output = Path.Combine(_directory, "output");
        foreach (var names in new string[][] { [], ["First", "missing"], [""], [null!] })
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() => ConvertAsync(format, input, output, names));
            Assert.False(File.Exists(output));
            Assert.False(Directory.Exists(output));
        }

        await Assert.ThrowsAnyAsync<ArgumentException>(() => ConvertAsync(format, input, output, ["First"], "First"));
        Assert.False(File.Exists(output));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static Task ConvertAsync(string format, string input, string output, string[] names, string? single = null) => format switch
    {
        "pdf" => ExcelConverter.ConvertToPdfAsync(input, output, new() { SheetName = single, SheetNames = names }),
        "image" => ExcelConverter.ConvertToImagesAsync(input, output, new() { SheetName = single, SheetNames = names, Dpi = 36 }),
        "svg" => ExcelConverter.ConvertToSvgAsync(input, output, new() { SheetName = single, SheetNames = names }),
        "ps" => ExcelConverter.ConvertToPostScriptAsync(input, output, new() { SheetName = single, SheetNames = names }),
        _ => ExcelConverter.ConvertToMarkdownAsync(input, output, new MarkdownExportOptions { SheetName = single, SheetNames = names }),
    };

    private string CreateWorkbook()
    {
        Directory.CreateDirectory(_directory);
        using var stream = SdkWorkbook.Create(Sheet("First"), SdkWorkbook.Styles(), name: "First");
        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            SdkWorkbook.AddSheet(document.WorkbookPart!, Sheet("Second"), "Second");
            SdkWorkbook.AddSheet(document.WorkbookPart!, Sheet("Third"), "Third");
        }
        var path = Path.Combine(_directory, "input.xlsx");
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private static S.Worksheet Sheet(string name) => new(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", name + "Value")) { RowIndex = 1 }));
}
