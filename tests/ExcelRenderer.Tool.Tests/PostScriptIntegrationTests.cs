using System.Text;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("ps")]
    [InlineData("render")]
    public async Task PostScript_commands_create_one_file_and_preserve_manifest_and_overwrite_rules(string command)
    {
        var output = Path.Combine(_directory, "report.ps");
        var manifest = Path.Combine(_directory, "manifest.json");
        var arguments = new List<string> { command, Input, "-o", output, "--no-system-fonts" };
        if (command == "render") arguments.AddRange(["--format", "ps", "--manifest", manifest]);
        AssertSuccess(await RunAsync(arguments.ToArray()));
        var ps = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(output));
        Assert.StartsWith("%!PS-Adobe-3.0", ps);
        Assert.Contains("/FontType 42", ps);
        if (command == "render") Assert.Contains("application/postscript", await File.ReadAllTextAsync(manifest));
        if (command == "ps")
        {
            var rejected = await RunAsync(arguments.ToArray());
            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Equal(ps, Encoding.ASCII.GetString(await File.ReadAllBytesAsync(output)));
        }
    }

    [Fact]
    public async Task PostScript_rejects_continuous_layout_and_cleans_spill_files()
    {
        var output = Path.Combine(_directory, "report.ps");
        var invalid = await RunAsync("render", Input, "-o", output, "--format", "ps", "--image-layout", "continuous");
        Assert.NotEqual(0, invalid.ExitCode);
        Assert.False(File.Exists(output));
        var temporary = Path.Combine(_directory, "spool");
        Directory.CreateDirectory(temporary);
        AssertSuccess(await RunAsync("ps", Input, "-o", output, "--buffer-memory-threshold", "1", "--buffer-temp-directory", temporary));
        Assert.Empty(Directory.GetFiles(temporary));
        var rejected = await RunAsync("ps", Input, "-o", Path.Combine(_directory, "rejected.ps"), "--buffer-memory-threshold", "1", "--no-buffer-temp");
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(_directory, "rejected.ps")));
        Assert.Empty(Directory.GetFiles(temporary));
    }
}
