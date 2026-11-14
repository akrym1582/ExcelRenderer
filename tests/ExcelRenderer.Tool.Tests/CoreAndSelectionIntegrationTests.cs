using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.TestFixtures;
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
    [InlineData("render")]
    public async Task Commands_accept_multiple_sheet_names_and_reject_missing_names(string command)
    {
        var input = CreateMultiSheetInput();
        var output = Path.Combine(_directory, "multi-output");
        var args = new List<string> { command, input, "-o", output, "--sheet", "Third", "First", "--sheet", "Third" };
        if (command == "core") args.AddRange(["--font-file", CoreFont]);
        if (command == "render") args.AddRange(["--format", "pdf"]);
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
        else if (command == "render")
        {
            var text = PdfText.Read(File.ReadAllBytes(output));
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
    [InlineData("render")]
    [InlineData("xlsx")]
    public async Task Commands_report_details_and_full_errors_by_default(string command)
    {
        var args = new List<string> { command, "missing.xlsx", "-o", Path.Combine(_directory, "output") };
        if (command == "core") args.AddRange(["--font-file", CoreFont]);
        if (command == "render") args.AddRange(["--format", "pdf"]);
        if (command == "xlsx") args.AddRange(["--data", "missing.json"]);
        var result = await RunAsync(args.ToArray());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Starting " + command, result.Output);
        Assert.Contains("Completed in", result.Output);
        Assert.Matches(@"Metric managedAllocatedBytes: [1-9][0-9]*", result.Output);
        Assert.Contains("Exception", result.Error);
        Assert.Contains("   at ", result.Error);
    }

    [Fact]
    public async Task Core_command_writes_pdf_and_metrics_without_overwriting_existing_output()
    {
        var input = CreateMultiSheetInput();
        var output = Path.Combine(_directory, "nested", "core.pdf");
        var result = await RunAsync("core", input, "-o", output, "--font-file", CoreFont);
        AssertSuccess(result);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
        Assert.Contains("Core PDF: 3 pages", result.Output);
        Assert.Contains("Metric coreGeometryPlan: 3.000", result.Output);
        Assert.Contains("Metric pagePayloadActive: 0.000", result.Output);
        Assert.Contains("Metric pagePayloadMax: 1.000", result.Output);
        Assert.Contains("Completed in", result.Output);
        Assert.Matches(@"Metric managedAllocatedBytes: [1-9][0-9]*", result.Output);
        Assert.Contains("Metric reader.ms:", result.Output);
        Assert.Contains("Metric pdfFinalSave.ms:", result.Output);
        Assert.Empty(result.Error);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, "*.tmp"));
        var bytes = File.ReadAllBytes(output);
        var rejected = await RunAsync("core", input, "-o", output, "--font-file", CoreFont);
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("already exists", rejected.Error);
        Assert.Equal(bytes, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Core_defaults_to_bundled_font_and_cleans_up_after_invalid_font()
    {
        var input = CreateMultiSheetInput();
        var output = Path.Combine(_directory, "core.pdf");
        Assert.Equal(0, (await RunAsync("core", input, "-o", output)).ExitCode);
        var bundledPdf = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output));
        Assert.StartsWith("%PDF", bundledPdf);
        Assert.Contains("NotoSansJP", bundledPdf);

        File.Delete(output);
        Assert.NotEqual(0, (await RunAsync("core", input, "-o", output, "--font-file", "missing.ttf")).ExitCode);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp.ttf"));
        var invalidInput = Path.Combine(_directory, "invalid.xlsx");
        await File.WriteAllTextAsync(invalidInput, "invalid workbook");
        Assert.NotEqual(0, (await RunAsync("core", invalidInput, "-o", output)).ExitCode);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp.ttf"));
        Assert.NotEqual(0, (await RunAsync("core", input, "-o", output, "--font-collection-index", "1")).ExitCode);
        Assert.False(File.Exists(output));
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
