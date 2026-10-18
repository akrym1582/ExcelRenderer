using System.IO.Compression;
using System.Xml.Linq;
using ExcelRenderer.TestFixtures;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData("xlsx")]
    [InlineData("md")]
    public async Task MappingCommandsApplyValuesBeforeRecalculation(string command)
    {
        Directory.CreateDirectory(_directory);
        var input = Path.Combine(_directory, "mapping-formula.xlsx");
        var data = Path.Combine(_directory, "mapping-formula.json");
        var output = Path.Combine(_directory, "mapping-result." + command);
        RetainedWorkbook.Copy("MappingIntegrationTests.Json_mapping_completes_for_xlsx_and_all_render_commands.9FE6386EC61B8529.xlsx", input);
        await File.WriteAllTextAsync(data, """{"title":"MAPPED_VALUE","items":[{"name":"First","value":12.5},{"name":"Second","value":23}]}""");
        using (var zip = ZipFile.Open(input, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var stream = entry.Open()) xml = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var row = xml.Descendants(ns + "row").First();
            row.Add(new XElement(ns + "c", new XAttribute("r", "D1"), new XElement(ns + "f", "B3*2"), new XElement(ns + "v", "999")));
            entry.Delete();
            using var streamOut = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            xml.Save(streamOut);
        }
        AssertSuccess(await RunAsync(command, input, "--data", data, "-o", output, "--formula-mode", "recalculate"));
        if (command == "md") Assert.Contains("25", File.ReadAllText(output));
        else
        {
            using var zip = ZipFile.OpenRead(output);
            using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var xml = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.Equal("25", xml.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == "D1").Element(ns + "v")?.Value);
        }
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("pdf", "skiasharp")]
    [InlineData("render", "skiasharp")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("md")]
    [InlineData("render")]
    public async Task RenderingCommandsAcceptRecalculation(string command, string? backend = null)
    {
        Directory.CreateDirectory(_directory);
        var input = Path.Combine(_directory, "calculate.xlsx");
        RetainedWorkbook.Copy("FormulaModeIntegrationTests.RenderingCommandsAcceptSavedFormulaDisplay.F08CB6342C0F4ADB.xlsx", input);
        using (var zip = ZipFile.Open(input, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var stream = entry.Open()) xml = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var cell = xml.Descendants(ns + "c").Single();
            cell.SetAttributeValue("t", null);
            cell.Element(ns + "f")!.Value = "123*2";
            cell.Element(ns + "v")?.Remove();
            cell.Add(new XElement(ns + "v", "999"));
            entry.Delete();
            using var streamOut = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            xml.Save(streamOut);
        }
        var path = Path.Combine(_directory, command == "md" ? "calculated.md" : command == "pdf" ? "calculated.pdf" : "calculated");
        var args = new List<string> { command, input, "-o", path, "--formula-mode", "recalculate" };
        if (command == "render") args.AddRange(["--format", backend is null ? "markdown" : "pdf"]);
        if (backend is not null) args.AddRange(["--pdf-backend", backend]);
        AssertSuccess(await RunAsync(args.ToArray()));
        if (command == "md") Assert.Contains("246", File.ReadAllText(path));
        else Assert.True(File.Exists(path) || Directory.EnumerateFiles(path).Any());
    }

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
        foreach (var mode in new[] { "invalid", "legacy" })
        {
            var args = new List<string> { command, "does-not-exist.xlsx", "-o", output, "--formula-mode", mode };
            if (command == "render") args.AddRange(["--format", "pdf"]);
            var result = await RunAsync(args.ToArray());
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("--formula-mode", result.Error + result.Output);
            Assert.Equal("unchanged", File.ReadAllText(output));
        }
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("md")]
    [InlineData("render")]
    [InlineData("pdf", "skiasharp")]
    [InlineData("render", "skiasharp")]
    public async Task RenderingCommandsAcceptSavedFormulaDisplay(string command, string? backend = null)
    {
        Directory.CreateDirectory(_directory);
        var input = Path.Combine(_directory, "formula.xlsx");
        RetainedWorkbook.Copy("FormulaModeIntegrationTests.RenderingCommandsAcceptSavedFormulaDisplay.F08CB6342C0F4ADB.xlsx", input);

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
        var args = new List<string> { command, input, "-o", path };
        if (command == "render") args.AddRange(["--format", backend is null ? "markdown" : "pdf"]);
        if (backend is not null) args.AddRange(["--pdf-backend", backend]);
        AssertSuccess(await RunAsync(args.ToArray()));
        if (command == "md") Assert.Contains("SAVEDRESULT", File.ReadAllText(path));
        else if (command == "render" && backend is null) Assert.Contains("SAVEDRESULT", File.Exists(path) ? File.ReadAllText(path) : string.Join("", Directory.GetFiles(path, "*.md").Select(File.ReadAllText)));
        else Assert.True(File.Exists(path) || Directory.EnumerateFiles(path).Any());
    }
}
