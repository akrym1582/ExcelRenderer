using System.Diagnostics;
using System.Text;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed class ToolIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExcelRenderer.Tool.Tests", Guid.NewGuid().ToString("N"));
    private string Input => Path.Combine(AppContext.BaseDirectory, "SampleInputs", "sample.xlsx");

    [Fact]
    public async Task Pdf_command_creates_a_pdf()
    {
        var output = Path.Combine(_directory, "report.pdf");
        var result = await RunAsync("pdf", Input, "-o", output);
        AssertSuccess(result);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
    }

    [Fact]
    public async Task Image_command_creates_png_files()
    {
        var output = Path.Combine(_directory, "images");
        var result = await RunAsync("image", Input, "-o", output, "--dpi", "72");
        AssertSuccess(result);
        var png = Assert.Single(Directory.GetFiles(output, "*.png"));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, File.ReadAllBytes(png)[..4]);
    }

    [Fact]
    public async Task Md_alias_creates_markdown()
    {
        var output = Path.Combine(_directory, "nested", "report.md");
        var result = await RunAsync("md", Input, "-o", output);
        AssertSuccess(result);
        Assert.Contains("折り返し", await File.ReadAllTextAsync(output));
    }

    [Theory]
    [InlineData("md")]
    [InlineData("md", "not-found.xlsx", "-o", "output.md")]
    public async Task Invalid_input_fails(params string[] arguments) => Assert.NotEqual(0, (await RunAsync(arguments)).ExitCode);

    [Fact]
    public async Task Invalid_dpi_is_rejected_by_parser()
    {
        var result = await RunAsync("image", Input, "-o", Path.Combine(_directory, "images"), "--dpi", "0");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("greater than zero", result.Error);
    }

    [Fact]
    public async Task Unknown_sheet_fails_without_a_stack_trace()
    {
        var result = await RunAsync("image", Input, "-o", Path.Combine(_directory, "images"), "--sheet", "UnknownSheet");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Worksheet was not found", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
    }

    private static async Task<Result> RunAsync(params string[] arguments)
    {
        var root = FindRepositoryRoot();
        var tool = ResolveToolAssemblyPath(root);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(tool);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, await output, await error);
    }

    private static string ResolveToolAssemblyPath(string repositoryRoot)
    {
        var debug = Path.Combine(repositoryRoot, "src", "ExcelRenderer.Tool", "bin", "Debug", "net10.0", "ExcelRenderer.Tool.dll");
        if (File.Exists(debug)) return debug;

        var release = Path.Combine(repositoryRoot, "src", "ExcelRenderer.Tool", "bin", "Release", "net10.0", "ExcelRenderer.Tool.dll");
        if (File.Exists(release)) return release;

        throw new FileNotFoundException(
            $"Tool assembly not found. Checked both Debug and Release outputs:{Environment.NewLine}{debug}{Environment.NewLine}{release}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ExcelRenderer.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }

    private static void AssertSuccess(Result result) => Assert.True(result.ExitCode == 0, result.Output + result.Error);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed record Result(int ExitCode, string Output, string Error);
}
