using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("core", false, false)]
    [InlineData("pdf", false, false)]
    [InlineData("ps", false, false)]
    [InlineData("image", false, false)]
    [InlineData("svg", false, false)]
    [InlineData("render", false, false)]
    [InlineData("render", true, false)]
    [InlineData("render", false, true)]
    public async Task Command_count_sums_selected_pages_without_counting_preflight(string command, bool trim, bool continuous)
    {
        var input = CreateMultiSheetInput();
        var all = Arguments("all");
        var selected = Arguments("selected");
        selected.AddRange(["--sheet", "First"]);
        var allResult = await RunAsync(all.ToArray());
        var selectedResult = await RunAsync(selected.ToArray());
        AssertSuccess(allResult);
        AssertSuccess(selectedResult);
        var pageLines = allResult.Output.Split('\n').Where(line => line.StartsWith("Page ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, pageLines.Length);
        Assert.Single(selectedResult.Output.Split('\n'), line => line.StartsWith("Page ", StringComparison.Ordinal));
        foreach (var line in pageLines)
        {
            Assert.Matches(@"renderMs=[0-9]+\.[0-9]{3}", line);
            Assert.Matches(@"managedAllocatedBytes=[1-9][0-9]*", line);
            Assert.Contains("commands=", line);
        }

        foreach (var sheet in new[] { "First", "Second", "Third" })
        {
            Assert.Contains($"Sheet \"{sheet}\": pages=1", allResult.Output);
        }

        var singleCount = Count(selectedResult.Output);
        Assert.True(singleCount > 0);
        Assert.Equal(singleCount * 3, Count(allResult.Output));
        if (command == "core") Assert.Equal(1, singleCount);

        List<string> Arguments(string name)
        {
            var args = new List<string> { command, input, "-o", Path.Combine(_directory, name) };
            if (command == "core") args.AddRange(["--font-file", CoreFont]);
            if (command == "render") args.AddRange(["--format", continuous ? "png" : "pdf"]);
            if (trim) args.Add("--trim");
            if (continuous) args.AddRange(["--image-layout", "continuous"]);
            return args;
        }

        static double Count(string output) => double.Parse(
            output.Split('\n').Single(line => line.StartsWith("Metric commands:", StringComparison.Ordinal))["Metric commands:".Length..].Trim(),
            CultureInfo.InvariantCulture);
    }
    [Theory]
    [InlineData("core")]
    [InlineData("render")]
    public async Task Page_and_sheet_summaries_track_multiple_pages_and_command_totals(string command)
    {
        var input = CreateMultiSheetInput();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            var part = (WorksheetPart)workbook.GetPartById(workbook.Workbook.Sheets!.GetFirstChild<S.Sheet>()!.Id!);
            var data = part.Worksheet.GetFirstChild<S.SheetData>()!;
            for (var row = 2; row <= 150; row++)
            {
                data.Append(new S.Row(new S.Cell(new S.InlineString(new S.Text("Row " + row)))
                {
                    CellReference = "A" + row, DataType = S.CellValues.InlineString,
                }) { RowIndex = (uint)row });
            }
        }

        var args = new List<string> { command, input, "-o", Path.Combine(_directory, "pages.pdf"), "--sheet", "First" };
        if (command == "core") args.AddRange(["--font-file", CoreFont]);
        else args.AddRange(["--format", "pdf"]);
        var result = await RunAsync(args.ToArray());
        AssertSuccess(result);
        var pages = result.Output.Split('\n').Where(line => line.StartsWith("Page ", StringComparison.Ordinal)).ToArray();
        Assert.True(pages.Length > 1);
        for (var index = 0; index < pages.Length; index++)
        {
            Assert.Contains($"source={index + 1} output={index + 1}:", pages[index]);
        }

        var sheet = Assert.Single(result.Output.Split('\n'), line => line.StartsWith("Sheet ", StringComparison.Ordinal));
        Assert.Contains($"pages={pages.Length} ", sheet);
        var expected = pages.Sum(ReadCommands);
        Assert.Equal(expected, ReadCommands(sheet));
        var total = result.Output.Split('\n').Single(line => line.StartsWith("Metric commands:", StringComparison.Ordinal));
        Assert.Equal(expected, double.Parse(total["Metric commands:".Length..], CultureInfo.InvariantCulture));

        static double ReadCommands(string line) => double.Parse(Regex.Match(line, @"\bcommands=([0-9]+)").Groups[1].Value, CultureInfo.InvariantCulture);
    }

}
