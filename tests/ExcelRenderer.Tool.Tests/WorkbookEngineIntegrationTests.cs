using ExcelRenderer.TestFixtures;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("markdown")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task RemovedEngineOptionsFailBeforeOpeningInputDataOrOutput(string command)
    {
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, "sentinel");
        File.WriteAllText(destination, "unchanged");
        foreach (var options in new[]
        {
            new[] { "--reader-backend", "openxml" },
            new[] { "--mapping-backend", "auto" },
            new[] { "--verbose" },
        })
        {
            var args = new List<string> { command, "missing.xlsx", "-o", destination, "--data", "missing.json" };
            args.AddRange(options);
            if (command == "render") args.AddRange(["--format", "pdf"]);
            var result = await RunAsync(args.ToArray());
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Unrecognized", result.Error + result.Output);
            Assert.Contains(options[0], result.Error + result.Output);
            Assert.Equal("unchanged", File.ReadAllText(destination));
        }
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("markdown")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task CommandsUseOpenXmlWithoutEngineOptions(string command)
    {
        Directory.CreateDirectory(_directory);
        var help = await RunAsync(command, "--help");
        AssertSuccess(help);
        Assert.DoesNotContain("--reader-backend", help.Output);
        Assert.DoesNotContain("--mapping-backend", help.Output);
        Assert.DoesNotContain("--verbose", help.Output);
        Assert.Contains("--formula-mode", help.Output);

        var template = Path.Combine(_directory, "template.xlsx");
        RetainedWorkbook.Copy("BackendIntegrationTests.ExplicitOpenXmlMappingAndReaderReachAllCommands.68C70801705FC28D.xlsx", template);
        var data = Path.Combine(_directory, "data.json");
        await File.WriteAllTextAsync(data, """{"message":"MappedOpenXmlValue"}""");
        var output = Path.Combine(_directory, "result");
        var args = new List<string> { command, template, "-o", output, "--data", data };
        if (command == "render") args.AddRange(["--format", "markdown"]);
        var result = await RunAsync(args.ToArray());
        AssertSuccess(result);
        Assert.DoesNotContain("Mapping:", result.Error + result.Output);
        Assert.DoesNotContain("Reader:", result.Error + result.Output);
        Assert.True(File.Exists(output) || Directory.Exists(output));
        if (command is "markdown" or "md" or "render")
        {
            var markdown = command == "render" ? Directory.GetFiles(output, "*.md", SearchOption.AllDirectories).Single() : output;
            Assert.Contains("MappedOpenXmlValue", await File.ReadAllTextAsync(markdown));
        }
        else if (command == "xlsx")
        {
            using var input = File.OpenRead(output);
            using var reader = OpenXml.Reader.OpenXmlWorkbookReader.Open(input);
            Assert.Contains(reader.EnumerateCells(1), cell => cell.Value.Text == "MappedOpenXmlValue");
        }
    }
}
