using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("core")]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("ps")]
    [InlineData("markdown")]
    [InlineData("render")]
    public async Task Commands_accept_multiple_sheet_names_and_reject_missing_names(string command)
    {
        var input = CreateMultiSheetInput();
        var output = Path.Combine(_directory, "multi-output");
        var args = new List<string> { command, input, "-o", output, "--sheet", "Third", "First", "--sheet", "Third" };
        if (command == "core") args.AddRange(["--font-file", CoreFont]);
        if (command == "render") args.AddRange(["--format", "markdown"]);
        if (command == "image") args.AddRange(["--dpi", "36"]);
        var result = await RunAsync(args.ToArray());
        AssertSuccess(result);
        Assert.DoesNotContain("Starting", result.Error);
        Assert.DoesNotContain("Metric", result.Error);
        if (command is "image" or "svg")
        {
            var files = Directory.GetFiles(output);
            Assert.Equal(2, files.Length);
            Assert.DoesNotContain(files, name => name.Contains("Second", StringComparison.Ordinal));
        }
        else if (command is "markdown" or "render")
        {
            var markdown = command == "render" ? Directory.GetFiles(output, "*.md").Single() : output;
            var text = await File.ReadAllTextAsync(markdown);
            Assert.DoesNotContain("SecondValue", text);
            Assert.True(text.IndexOf("ThirdValue", StringComparison.Ordinal) < text.IndexOf("FirstValue", StringComparison.Ordinal));
        }
        else
        {
            Assert.True(new FileInfo(output).Length > 10);
        }

        args[3] = Path.Combine(_directory, "invalid-output");
        args.AddRange(["--sheet", "missing"]);
        var rejected = await RunAsync(args.ToArray());
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("Worksheet was not found", rejected.Error);
        Assert.False(File.Exists(args[3]));
        Assert.False(Directory.Exists(args[3]));
    }

    [Theory]
    [InlineData("core")]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("ps")]
    [InlineData("markdown")]
    [InlineData("render")]
    [InlineData("xlsx")]
    public async Task Verbose_reports_command_details_and_full_errors(string command)
    {
        var args = new List<string> { command, "missing.xlsx", "-o", Path.Combine(_directory, "output"), "--verbose" };
        if (command == "core") args.AddRange(["--font-file", CoreFont]);
        if (command == "render") args.AddRange(["--format", "pdf"]);
        if (command == "xlsx") args.AddRange(["--data", "missing.json"]);
        var result = await RunAsync(args.ToArray());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Starting " + command, result.Error);
        Assert.Contains("Exception", result.Error);
        Assert.Contains("   at ", result.Error);
    }

    [Fact]
    public async Task Core_command_writes_pdf_and_verbose_metrics_without_overwriting_existing_output()
    {
        var input = CreateMultiSheetInput();
        var output = Path.Combine(_directory, "nested", "core.pdf");
        var result = await RunAsync("core", input, "-o", output, "--font-file", CoreFont, "--verbose");
        AssertSuccess(result);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
        Assert.Contains("Core PDF: 3 pages", result.Error);
        Assert.Contains("Completed in", result.Error);
        Assert.Contains("Metric reader.ms:", result.Error);
        Assert.Contains("Metric pdfFinalSave.ms:", result.Error);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, "*.tmp"));
        var bytes = File.ReadAllBytes(output);
        var rejected = await RunAsync("core", input, "-o", output, "--font-file", CoreFont);
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("already exists", rejected.Error);
        Assert.Equal(bytes, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Core_requires_font_and_cleans_up_after_invalid_font()
    {
        var input = CreateMultiSheetInput();
        var output = Path.Combine(_directory, "core.pdf");
        Assert.NotEqual(0, (await RunAsync("core", input, "-o", output)).ExitCode);
        Assert.NotEqual(0, (await RunAsync("core", input, "-o", output, "--font-file", "missing.ttf")).ExitCode);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private static string CoreFont => Path.Combine(FindRepositoryRoot(), "third_party", "NotoSansJP", "NotoSansJP-Regular.ttf");

    private string CreateMultiSheetInput()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "multi.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new S.Workbook(new S.Sheets());
        foreach (var name in new[] { "First", "Second", "Third" })
        {
            var part = workbook.AddNewPart<WorksheetPart>();
            part.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell(new S.InlineString(new S.Text(name + "Value"))) { CellReference = "A1", DataType = S.CellValues.InlineString }) { RowIndex = 1 }));
            workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = (uint)workbook.Workbook.Sheets.ChildElements.Count + 1, Name = name });
        }

        return path;
    }
}
