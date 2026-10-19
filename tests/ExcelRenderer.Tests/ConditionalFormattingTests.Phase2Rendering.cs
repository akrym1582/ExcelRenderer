using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.Rendering;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed partial class ConditionalFormattingTests
{
    public static IEnumerable<object[]> PopulationOutputCases =>
        from type in new[] { "duplicateValues", "uniqueValues", "top10", "aboveAverage", "colorScale" }
        from format in new[] { OutputFormat.Pdf, OutputFormat.Png, OutputFormat.Svg }
        select new object[] { type, format };

    [Theory]
    [MemberData(nameof(PopulationOutputCases))]
    public async Task MappingPopulationRulesSurviveSaveReloadAndAllVisualFormats(string type, OutputFormat format)
    {
        var rule = type == "colorScale" ? Scale("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "000000", "FFFFFF", ranges: "A1") : Rule(type, "", ranges: "A1", extra: type == "top10" ? "rank=\"2\"" : "");
        using var template = Book("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>**items[*]</t></is></c></row>", rule);
        using var mapped = new MemoryStream();
        Mapping.ExcelTemplateMapper.Map(template, mapped, new { items = new[] { 10, 20, 20, 30 } });
        mapped.Position = 0;
        using (var reader = OpenXmlWorkbookReader.Open(mapped))
        {
            var saved = Assert.Single(reader.ReadWorksheetInfo(1).ConditionalFormattingRules);
            Assert.Equal(4, saved.Ranges.Sum(range => range.Last.Row - range.First.Row + 1));
        }

        var sheet = Read(mapped);
        Assert.Empty(sheet.CellReadDiagnostics);
        Assert.Equal(type switch { "duplicateValues" => "0110", "uniqueValues" => "1001", "top10" => "0111", "aboveAverage" => "0001", _ => "1111" }, Painted(sheet, 4));
        if (type == "colorScale")
        {
            Assert.Equal(new ReportColor(128, 128, 128), sheet.Cells[new(2, 1)].Style.Background);
            Assert.Equal(new ReportColor(128, 128, 128), sheet.Cells[new(3, 1)].Style.Background);
        }

        mapped.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(mapped, new() { OutputFormat = format, DiagnosticOptions = new() { DetectUnsupportedFeatures = true } }, new SingleStreamOutputSink(output));
        Assert.True(output.Length > 100);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("ConditionalFormatting", StringComparison.Ordinal) || diagnostic.Code == "UnsupportedConditionalFormatting");
        if (type == "colorScale")
        {
            var directory = Environment.GetEnvironmentVariable("EXCELRENDERER_CF_PHASE2_ARTIFACTS");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, "mapped-scale.xlsx"), mapped.ToArray());
                await File.WriteAllBytesAsync(Path.Combine(directory, "mapped-scale." + format.ToString().ToLowerInvariant()), output.ToArray());
            }
        }
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    public async Task RepresentativePopulationRulesRenderWithIndependentAttributes(OutputFormat format)
    {
        int[][] values = [[10, 20, 20, 30], [100, 90, 90, 80], [0, 10, 20, 0], [0, 25, 50, 100], [0, 1, 2, 100], [0, 1, 2, 100]];
        var rows = string.Concat(Enumerable.Range(0, 4).Select(row => $"<row r=\"{row + 1}\" ht=\"30\" customHeight=\"1\">" + string.Concat(Enumerable.Range(0, 6).Select(column => $"<c r=\"{(char)('A' + column)}{row + 1}\"><v>{values[column][row]}</v></c>")) + "</row>"));
        var rules = Rule("duplicateValues", "", ranges: "A1:A4") + Rule("top10", "", priority: 2, ranges: "B1:B4", extra: "rank=\"2\"") + Rule("aboveAverage", "", priority: 3, ranges: "C1:C4") +
            Scale("<cfvo type=\"min\"/><cfvo type=\"num\" val=\"50\"/><cfvo type=\"max\"/>", "000000", "00FF00", "FFFFFF", ranges: "D1:D4", priority: 4) +
            Scale("<cfvo type=\"min\"/><cfvo type=\"percent\" val=\"50\"/><cfvo type=\"max\"/>", "000000", "00FF00", "FFFFFF", ranges: "E1:E4", priority: 5) +
            Scale("<cfvo type=\"min\"/><cfvo type=\"percentile\" val=\"50\"/><cfvo type=\"max\"/>", "000000", "00FF00", "FFFFFF", ranges: "F1:F4", priority: 6);
        using var input = Book(rows, rules);
        var sheet = Read(input);
        Assert.Equal("0110", Painted(sheet, 4));
        Assert.Equal(new ReportColor(0, 128, 0), sheet.Cells[new(2, 4)].Style.Background);
        Assert.Equal(new ReportColor(0, 5, 0), sheet.Cells[new(2, 5)].Style.Background);
        Assert.Equal(new ReportColor(0, 170, 0), sheet.Cells[new(2, 6)].Style.Background);
        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = format }, new SingleStreamOutputSink(output));
        var directory = Environment.GetEnvironmentVariable("EXCELRENDERER_CF_PHASE2_ARTIFACTS");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "representative.xlsx"), input.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, "representative." + format.ToString().ToLowerInvariant()), output.ToArray());
        }
    }

    [Fact]
    public void MappingThresholdValReferencesMoveWithTheOnePopulationAnchor()
    {
        using var template = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>**items[*]</t></is></c></row><row r=\"3\"><c r=\"D3\"><v>0</v></c><c r=\"E3\"><v>100</v></c></row>",
            Scale("<cfvo type=\"formula\" val=\"$D$3\"/><cfvo type=\"formula\" val=\"$E$3\"/>", "000000", "FFFFFF", ranges: "A2 C2"));
        using var mapped = new MemoryStream();
        Mapping.ExcelTemplateMapper.Map(template, mapped, new { items = new[] { 0, 50, 100 } });
        mapped.Position = 0;
        using (var reader = OpenXmlWorkbookReader.Open(mapped))
        {
            var rule = Assert.Single(reader.ReadWorksheetInfo(1).ConditionalFormattingRules);
            var points = rule.Source.Children.Single(child => child.Name == "colorScale").Children.Where(child => child.Name == "cfvo").ToArray();
            Assert.Equal(new[] { "$D$5", "$E$5" }, points.Select(point => point.Attributes["val"]));
            Assert.Equal(6, rule.Ranges.Sum(range => (range.Last.Row - range.First.Row + 1) * (range.Last.Column - range.First.Column + 1)));
        }

        Assert.Equal(new ReportColor(128, 128, 128), Read(mapped).Cells[new(3, 1)].Style.Background);
    }

    [Fact]
    public void DynamicBackgroundIsPresentInSharedDrawingCommandsAndPreservesRichText()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>0</v></c><c r=\"B1\" t=\"inlineStr\"><is><r><rPr><b/></rPr><t>Rich</t></r></is></c></row><row r=\"2\"><c r=\"A2\"><v>50</v></c></row><row r=\"3\"><c r=\"A3\"><v>100</v></c></row>",
            Rule("expression", "<formula>TRUE</formula>", priority: 1, dxf: 1, ranges: "A1:B3") + Scale("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "000000", "FFFFFF", ranges: "A1:B3", priority: 2), [Yellow, Red]);
        var sheet = Read(input);
        Assert.Null(sheet.Cells[new(1, 2)].Style.Background);
        Assert.True(sheet.Cells[new(1, 2)].RichTextRuns!.Single().Font.Bold);
        var measurer = new SkiaSharp.SkiaTextMeasurer(new Fonts.FontManager(new Fonts.FontOptions()));
        var document = new Layout.ReportLayoutEngine(measurer).Layout(sheet);
        var commands = new Drawing.DrawCommandGeneratorPass().Generate(document);
        Assert.Contains(commands, command => command is Drawing.FillRectangleCommand fill && fill.Color == new ReportColor(128, 128, 128));
        Assert.Contains(commands, command => command is Drawing.DrawTextCommand text && text.Style.Font.Color == new ReportColor(255, 0, 0));
    }

    [Fact]
    public async Task PageSelectionKeepsOffPageValuesInScalePopulation()
    {
        using var input = Book(NumericRows("0,50,100"), Scale("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "000000", "FFFFFF", ranges: "A1:A3"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            sheet.Append(new S.RowBreaks(new S.Break { Id = 2, Min = 0, Max = 16383, ManualPageBreak = true }));
            package.WorkbookPart.Workbook.Append(new S.DefinedNames(new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "data!$A$1:$A$3" }));
        }

        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Svg, Selection = new() { Pages = [1] } }, new SingleStreamOutputSink(output));
        using var svg = new Svg.Skia.SKSvg();
        using var svgInput = new MemoryStream(output.ToArray());
        using var bitmap = Raster(svg.Load(svgInput)!);
        Assert.True(bitmap.Pixels.Count(pixel => pixel.Red == 128 && pixel.Green == 128 && pixel.Blue == 128) > 100);
    }

    [Fact]
    public async Task CoreAndDisabledMainDoNotBuildPopulationOrSort()
    {
        using var input = Book(NumericRows("0,50,100"), Rule("top10", "", ranges: "A1:A3") + Scale("<cfvo type=\"min\"/><cfvo type=\"percentile\" val=\"50\"/><cfvo type=\"max\"/>", "000000", "00FF00", "FFFFFF", ranges: "A1:A3", priority: 2));
        var metrics = new List<string>();
        ConversionMetrics.Observer = (name, _) => metrics.Add(name);
        try
        {
            Assert.Equal("000", Painted(new ExcelReader().Read(input, new() { ApplyConditionalFormatting = false }).Sheets[0], 3));
            input.Position = 0;
            using var output = new MemoryStream();
            await Core.ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf") });
            Assert.DoesNotContain(metrics, name => name.StartsWith("conditionalFormatting", StringComparison.Ordinal));
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }
}
