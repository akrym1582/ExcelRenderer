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
    [InlineData("md")]
    [InlineData("render")]
    public async Task RemovedMappingBackendShowsMigrationBeforeOpeningInputOrData(string command)
    {
        Directory.CreateDirectory(_directory);
        var destination=Path.Combine(_directory,"sentinel");File.WriteAllText(destination,"unchanged");
        var args=new List<string>{command,"does-not-exist.xlsx","-o",destination,"--mapping-backend","closedxml","--data","does-not-exist.json"};
        if(command=="render")args.AddRange(["--format","pdf"]);
        var result=await RunAsync(args.ToArray());
        Assert.NotEqual(0,result.ExitCode);Assert.Contains("ClosedXML has been removed",result.Error+result.Output);
        Assert.Equal("unchanged",File.ReadAllText(destination));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task RemovedReaderBackendShowsMigrationBeforeOpeningInputOrData(string command)
    {
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, "sentinel");
        File.WriteAllText(destination, "unchanged");
        var args = new List<string> { command, "does-not-exist.xlsx", "-o", destination, "--reader-backend", "closedxml", "--data", "does-not-exist.json" };
        if (command == "render") args.AddRange(["--format", "pdf"]);
        var result = await RunAsync(args.ToArray());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ClosedXML has been removed", result.Error + result.Output);
        Assert.Contains("openxml or auto", result.Error + result.Output);
        Assert.Equal("unchanged", File.ReadAllText(destination));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task BackendOptionsRejectMissingDataAndIncompatibleFormulaMode(string command)
    {
        Directory.CreateDirectory(_directory);
        foreach (var options in new[] { new[] { "--mapping-backend", "closedxml" }, new[] { "--reader-backend", "invalid" } })
        {
            var args = new List<string> { command, "does-not-exist.xlsx", "-o", Path.Combine(_directory, "untouched") };
            args.AddRange(options);
            if (command == "render") args.AddRange(["--format", "pdf"]);
            var result = await RunAsync(args.ToArray());
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(options[0], result.Error + result.Output);
            Assert.False(File.Exists(Path.Combine(_directory, "untouched")));
        }
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task ExplicitOpenXmlMappingAndReaderReachAllCommands(string command)
    {
        Directory.CreateDirectory(_directory);
        var template = Path.Combine(_directory, "template.xlsx");
        RetainedWorkbook.Copy("BackendIntegrationTests.ExplicitOpenXmlMappingAndReaderReachAllCommands.68C70801705FC28D.xlsx", template);

        var data = Path.Combine(_directory, "data.json");
        await File.WriteAllTextAsync(data, """{"message":"OPENXML_VALUE"}""");
        var output = Path.Combine(_directory, "result");
        var args = new List<string> { command, template, "-o", output, "--data", data, "--mapping-backend", "openxml", "--verbose" };
        if (command != "xlsx") args.AddRange(["--reader-backend", "openxml"]);
        if (command == "render") args.AddRange(["--format", "markdown"]);
        var result = await RunAsync(args.ToArray());
        AssertSuccess(result);
        Assert.Contains("Mapping: OpenXml", result.Error);
        if (command != "xlsx") Assert.Contains("Reader: OpenXml", result.Error);
        Assert.DoesNotContain("Mapping:", result.Output);
        Assert.True(File.Exists(output) || Directory.Exists(output));
    }
}
