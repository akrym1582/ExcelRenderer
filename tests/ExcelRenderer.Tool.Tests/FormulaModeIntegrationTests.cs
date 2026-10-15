using System.IO.Compression;
using System.Xml.Linq;
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
    public async Task FormulaModeIsValidatedBeforeInputAndOutput(string command)
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "sentinel");
        File.WriteAllText(output, "unchanged");
        var args = new List<string> { command, "does-not-exist.xlsx", "-o", output, "--formula-mode", "invalid" };
        if (command == "render") args.AddRange(["--format", "pdf"]);
        var result = await RunAsync(args.ToArray());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--formula-mode", result.Error + result.Output);
        Assert.Equal("unchanged", File.ReadAllText(output));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task RenderingCommandsAcceptSavedFormulaDisplay(string command)
    {
        Directory.CreateDirectory(_directory);
        var input = Path.Combine(_directory, "formula.xlsx");
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("formula").Cell(1, 1).FormulaA1 = "1+1";
            workbook.SaveAs(input);
        }

        using (var zip = ZipFile.Open(input, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var stream = entry.Open()) xml = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var cell = xml.Descendants(ns + "c").Single();
            cell.SetAttributeValue("t", "str");
            cell.Element(ns + "v")?.Remove();
            cell.Add(new XElement(ns + "v", "SAVEDRESULT"));
            entry.Delete();
            using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            xml.Save(output);
        }

        var path = Path.Combine(_directory, command == "md" ? "result.md" : command == "pdf" ? "result.pdf" : "result");
        var args = new List<string> { command, input, "-o", path, "--formula-mode", "cached" };
        if (command == "render") args.AddRange(["--format", "markdown"]);
        AssertSuccess(await RunAsync(args.ToArray()));
        if (command == "md") Assert.Contains("SAVEDRESULT", File.ReadAllText(path));
        else if (command == "render") Assert.Contains("SAVEDRESULT", File.Exists(path) ? File.ReadAllText(path) : string.Join("", Directory.GetFiles(path, "*.md").Select(File.ReadAllText)));
        else Assert.True(File.Exists(path) || Directory.EnumerateFiles(path).Any());
    }
}
