using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("pdf")]
    [InlineData("svg")]
    [InlineData("image")]
    [InlineData("ps")]
    [InlineData("render")]
    public async Task Column_metrics_options_are_forwarded_to_every_render_command(string command)
    {
        var output = Path.Combine(_directory, command == "pdf" ? "metrics.pdf" : command == "ps" ? "metrics.ps" : "metrics");
        var args = new List<string> { command, Input, "-o", output, "--column-width-mdw", "16", "--column-width-metrics", "source", "--layout-font", Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", "collection-distinct.ttc"), "--layout-font-index", "1" };
        if (command == "render") args.AddRange(["--format", "svg"]);
        AssertSuccess(await RunAsync(args.ToArray()));
    }

    [Theory]
    [InlineData("--column-width-mdw", "0")]
    [InlineData("--column-width-mdw", "NaN")]
    [InlineData("--column-width-metrics", "invalid")]
    [InlineData("--layout-font-index", "1")]
    [InlineData("--layout-font", "missing.ttf")]
    public async Task Invalid_column_metrics_fail_with_clear_errors(string option, string value)
    {
        var result = await RunAsync("pdf", Input, "-o", Path.Combine(_directory, "invalid.pdf"), option, value);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_directory, "invalid.pdf")));
    }
}
