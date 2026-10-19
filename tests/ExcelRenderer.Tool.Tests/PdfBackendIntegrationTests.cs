using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("pdf")]
    [InlineData("render")]
    public async Task Explicit_nonzero_collection_index_reaches_the_pdf_writer(string command)
    {
        Directory.CreateDirectory(_directory);
        var input = Path.Combine(_directory, "collection.xlsx");
        File.Copy(Input, input);
        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
        {
            foreach (var font in document.WorkbookPart!.WorkbookStylesPart!.Stylesheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.FontName>())
                font.Val = "PDF Format Fixture";
            foreach (var part in document.WorkbookPart.WorksheetParts)
                foreach (var text in part.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>()) text.Text = "A B";
            if (document.WorkbookPart.SharedStringTablePart is { } strings)
                foreach (var text in strings.SharedStringTable.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>()) text.Text = "A B";
        }
        var output = Path.Combine(_directory, "collection.pdf");
        var arguments = new List<string>
        {
            command, input, "-o", output, "--font-file", Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats/collection-distinct.ttc"),
            "--font-face-index", "1", "--fallback-font", "PDF Format Fixture", "--no-system-fonts",
        };
        if (command == "render") arguments.AddRange(["--format", "pdf"]);
        var result = await RunAsync(arguments.ToArray());
        AssertSuccess(result);
        Assert.Contains("PdfFormatFixture-Bold", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output)));
    }

    [Theory]
    [InlineData("-1", 1)]
    [InlineData("1", 0)]
    [InlineData("1", 2)]
    [InlineData("2", 1)]
    public async Task Invalid_face_indices_and_ambiguous_file_lists_do_not_commit_output(string index, int files)
    {
        var output = Path.Combine(_directory, "rejected.pdf");
        var arguments = new List<string> { "pdf", Input, "-o", output, "--font-face-index", index };
        for (var i = 0; i < files; i++) arguments.AddRange(["--font-file", Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats/collection-distinct.ttc")]);
        var result = await RunAsync(arguments.ToArray());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(files != 1 || index == "-1" ? "--font-face-index" : "index", result.Error);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("pdf", "default")]
    [InlineData("pdf", "skiasharp")]
    [InlineData("render", "default")]
    [InlineData("render", "skiasharp")]
    public async Task Pdf_backend_reaches_both_cli_paths(string command, string backend)
    {
        var output = Path.Combine(_directory, "backend.pdf");
        var arguments = new List<string> { command, Input, "-o", output };
        if (backend != "default") arguments.AddRange(["--pdf-backend", backend]);
        if (command == "render") arguments.AddRange(["--format", "pdf"]);
        var result = await RunAsync(arguments.ToArray());
        AssertSuccess(result);
        var bytes = File.ReadAllBytes(output);
        Assert.True(bytes.Length > 0);
        Assert.Contains("Skia/PDF", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData("png", "pdfsharp")]
    [InlineData("svg", "skiasharp")]
    [InlineData("markdown", "pdfsharp")]
    [InlineData("pdf", "unknown")]
    [InlineData("pdf", "pdfsharp")]
    [InlineData("pdf", "SKIASHARP")]
    public async Task Invalid_backend_combinations_fail_during_parse(string format, string backend)
    {
        var output = Path.Combine(_directory, "invalid");
        var result = await RunAsync("render", "missing.xlsx", "-o", output, "--format", format, "--pdf-backend", backend);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--pdf-backend", result.Error);
        Assert.False(File.Exists(output));
        Assert.False(Directory.Exists(output));
    }
}
