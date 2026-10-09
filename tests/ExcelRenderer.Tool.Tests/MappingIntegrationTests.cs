using System.Text;
using ClosedXML.Excel;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("xlsx", null)]
    [InlineData("pdf", null)]
    [InlineData("svg", null)]
    [InlineData("image", null)]
    [InlineData("render", "pdf")]
    [InlineData("render", "svg")]
    [InlineData("render", "png")]
    [InlineData("markdown", null)]
    public async Task Json_mapping_completes_for_xlsx_and_all_render_commands(string command, string? format)
    {
        Directory.CreateDirectory(_directory);
        var template = Path.Combine(_directory, "template.xlsx");
        var data = Path.Combine(_directory, "data.json");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Mapped");
            sheet.Cell("A1").Value = "**title";
            sheet.Cell("A2").Value = "**@start-array items[*] as item";
            sheet.Cell("A3").Value = "**@item.name";
            sheet.Cell("B3").Value = "**@item.value | format(\"N2\")";
            sheet.Cell("A4").Value = "**@end-array";
            sheet.Column(1).Width = 25;
            sheet.PageSetup.PrintAreas.Add("A1:B4");
            workbook.SaveAs(template);
        }

        await File.WriteAllTextAsync(data, """{"title":"MAPPED_VALUE","items":[{"name":"First","value":12.5},{"name":"Second","value":23}]}""");
        var outputFormat = format ?? (command == "image" ? "png" : command);
        var output = Path.Combine(_directory, outputFormat is "pdf" or "xlsx" or "markdown" ? "result." + outputFormat : "render-output");
        var args = new List<string> { command, template, "--data", data, "-o", output };
        if (format is not null)
        {
            args.AddRange(["--format", format]);
        }

        if (command == "image")
        {
            args.AddRange(["--dpi", "72"]);
        }

        AssertSuccess(await RunAsync(args.ToArray()));
        switch (outputFormat)
        {
            case "xlsx":
                using (var workbook = new XLWorkbook(output))
                {
                    var sheet = workbook.Worksheet(1);
                    Assert.Equal("MAPPED_VALUE", sheet.Cell("A1").GetString());
                    Assert.Equal("Second", sheet.Cell("A3").GetString());
                    Assert.Equal("12.50", sheet.Cell("B2").GetString());
                }

                break;
            case "pdf":
                Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
                break;
            case "png":
                Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, File.ReadAllBytes(Assert.Single(Directory.GetFiles(output, "*.png")))[..4]);
                break;
            case "svg":
                var svg = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(output, "*.svg")));
                Assert.Contains("<path", svg);
                Assert.DoesNotContain("**@", svg);
                break;
            case "markdown":
                Assert.Contains("MAPPED\\_VALUE", await File.ReadAllTextAsync(output));
                break;
        }
    }

    [Theory]
    [InlineData("png")]
    [InlineData("svg")]
    public async Task Mapping_page_breaks_produce_four_pages_even_with_fit_to_page_template(string format)
    {
        Directory.CreateDirectory(_directory);
        var template = Path.Combine(_directory, "breaks.xlsx");
        var data = Path.Combine(_directory, "data.json");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Breaks");
            sheet.Cell("A1").Value = "Upper left";
            sheet.Cell("D1").Value = "Upper right";
            sheet.Cell("C2").Value = "**@page-break";
            sheet.Cell("A3").Value = "Lower left";
            sheet.Cell("D3").Value = "Lower right";
            sheet.PageSetup.PrintAreas.Add("A1:D3");
            sheet.PageSetup.FitToPages(1, 1);
            workbook.SaveAs(template);
        }

        await File.WriteAllTextAsync(data, "{}");
        var output = Path.Combine(_directory, "pages");
        AssertSuccess(await RunAsync("render", template, "--data", data, "--format", format, "-o", output));
        Assert.Equal(4, Directory.GetFiles(output, "*." + format).Length);
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    [InlineData("render")]
    public async Task Mapping_errors_preserve_existing_output_and_reject_input_overwrite(string command)
    {
        Directory.CreateDirectory(_directory);
        var template = Path.Combine(_directory, "invalid.xlsx");
        var data = Path.Combine(_directory, "data.json");
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Invalid").Cell("B4").Value = "**missing";
            workbook.SaveAs(template);
        }

        await File.WriteAllTextAsync(data, "{}");
        var output = Path.Combine(_directory, "preserve.xlsx");
        await File.WriteAllTextAsync(output, "sentinel");
        string[] extra = command == "render" ? ["--format", "pdf"] : [];
        var result = await RunAsync([command, template, "--data", data, "-o", output, .. extra]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid!B4", result.Error);
        Assert.Equal("sentinel", await File.ReadAllTextAsync(output));
        var original = await File.ReadAllBytesAsync(template);
        result = await RunAsync([command, template, "--data", data, "-o", template, .. extra]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(template));
        result = await RunAsync([command, template, "--data", data, "-o", data, .. extra]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("{}", await File.ReadAllTextAsync(data));
    }
}
