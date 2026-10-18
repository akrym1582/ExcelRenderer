using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
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
