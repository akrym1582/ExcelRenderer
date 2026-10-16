using ClosedXML.Excel;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task BackendOptionsRejectMissingDataAndIncompatibleFormulaMode(string command)
    {
        Directory.CreateDirectory(_directory);
        foreach (var options in new[] { new[] { "--mapping-backend", "closedxml" }, new[] { "--reader-backend", "openxml" } })
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
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("data").Cell("A1").Value = "**message";
            workbook.SaveAs(template);
        }

        var data = Path.Combine(_directory, "data.json");
        await File.WriteAllTextAsync(data, """{"message":"OPENXML_VALUE"}""");
        var output = Path.Combine(_directory, "result");
        var args = new List<string> { command, template, "-o", output, "--data", data, "--mapping-backend", "openxml", "--verbose" };
        if (command != "xlsx") args.AddRange(["--reader-backend", "openxml", "--formula-mode", "cached"]);
        if (command == "render") args.AddRange(["--format", "markdown"]);
        var result = await RunAsync(args.ToArray());
        AssertSuccess(result);
        Assert.Contains("Mapping: OpenXml", result.Error);
        if (command != "xlsx") Assert.Contains("Reader: OpenXml", result.Error);
        Assert.DoesNotContain("Mapping:", result.Output);
        Assert.True(File.Exists(output) || Directory.Exists(output));
    }
}
