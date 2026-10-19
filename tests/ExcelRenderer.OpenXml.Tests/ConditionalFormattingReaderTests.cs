using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class ConditionalFormattingReaderTests
{
    [Fact]
    public void DifferentialSnapshotsKeepOmittedFalseClearedAndIndexedColors()
    {
        var styles = Fixture.Styles.Replace("</styleSheet>", "<dxfs><dxf><font><b val=\"0\"/><u val=\"none\"/><color indexed=\"2\"/></font><fill><patternFill patternType=\"none\"/></fill><border><left/></border></dxf></dxfs></styleSheet>");
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><v>1</v></c>") + "<conditionalFormatting sqref=\"A1 B2:C4\"><cfRule type=\"expression\" priority=\"2\" stopIfTrue=\"1\" dxfId=\"0\"><formula>$A1=1</formula></cfRule></conditionalFormatting>", styles);
        var reader = OpenXmlWorkbookReader.Open(input);
        var info = reader.ReadWorksheetInfo(1);
        var rule = Assert.Single(info.ConditionalFormattingRules);
        Assert.Equal("A1:A1", rule.Ranges[0].ToString());
        Assert.Equal("B2:C4", rule.Ranges[1].ToString());
        Assert.Equal("$A1=1", Assert.Single(rule.Source.Children).Text);
        var font = rule.Style!.Source.Children.Single(child => child.Name == "font");
        Assert.Equal("0", font.Children.Single(child => child.Name == "b").Attributes["val"]);
        Assert.DoesNotContain(font.Children, child => child.Name == "i");
        Assert.Equal("FF0000", font.Children.Single(child => child.Name == "color").Attributes["resolvedRgb"]);
        reader.Dispose();
        Assert.Equal("none", font.Children.Single(child => child.Name == "u").Attributes["val"]);
    }

    [Fact]
    public void MissingDxfAndExtensionsRemainVisibleWithoutApplyingEitherTwice()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><v>1</v></c>") + """
            <conditionalFormatting sqref="A1"><cfRule type="expression" priority="1" dxfId="99"><formula>TRUE</formula><extLst><ext uri="id"><id xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">rule-id</id></ext></extLst></cfRule></conditionalFormatting>
            <extLst><ext uri="x14"><conditionalFormattings xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"><conditionalFormatting><cfRule type="dataBar" id="rule-id"/><sqref xmlns="http://schemas.microsoft.com/office/excel/2006/main">A1</sqref></conditionalFormatting></conditionalFormattings></ext></extLst>
            """, Fixture.Styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var rules = reader.ReadWorksheetInfo(1).ConditionalFormattingRules;
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule => Assert.True(rule.IsExtension));
        Assert.Null(rules[0].Style);
    }

    [Fact]
    public void SavedValueSessionNeverRecalculatesAndReportsMissingCache()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><f>1+1</f><v>99</v></c><c r=\"B1\"><f>1+1</f></c>"), Fixture.Styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var saved = WorkbookCalculationSession.CreateSavedValueSession(reader);
        var recalculate = WorkbookCalculationSession.Create(reader);
        var parsed = FormulaParser.Parse("A1=99").Formula!;
        var origin = new FormulaCellAddress("Sheet1", 1, 1);
        Assert.True(saved.EvaluateFormula(parsed, origin, origin).Value.Boolean);
        Assert.False(recalculate.EvaluateFormula(parsed, origin, origin).Value.Boolean);
        var missing = saved.EvaluateFormula(FormulaParser.Parse("B1=0").Formula!, origin, origin);
        Assert.False(missing.Success);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingCachedValue", StringComparison.Ordinal));
        Assert.Equal(FormulaValueKind.Blank, saved.ReadValue(new("Sheet1", 3, 3)).Kind);
    }

    [Fact]
    public void SparseValueEnumerationRetainsMissingFormulaAndMetadataButNeverExpandsBlanks()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><v>1</v></c><c r=\"B1\"/><c r=\"C1\" t=\"str\"><f>\"\"</f><v/></c><c r=\"D1\"><f>1+1</f></c><c r=\"E1\" cm=\"1\"><v>3</v></c>"), Fixture.Styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var saved = WorkbookCalculationSession.CreateSavedValueSession(reader);
        var addresses = saved.EnumerateValueCells(new("Sheet1", 1, 1, 1048576, 16384)).ToArray();
        Assert.Equal(new[] { 1, 3, 4, 5 }, addresses.Select(address => address.Column));
        Assert.Equal(FormulaValueKind.Text, saved.ReadValue(addresses[1]).Kind);
        Assert.Equal("", saved.ReadValue(addresses[1]).Text);
        Assert.Throws<FormulaCalculationException>(() => saved.ReadValue(addresses[2]));
        Assert.Throws<FormulaCalculationException>(() => saved.ReadValue(addresses[3]));
        Assert.Equal(2, WorkbookCalculationSession.Create(reader).ReadValue(addresses[2]).Number);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => saved.EnumerateValueCells(new("Sheet1", 1, 1, 1048576, 16384), cancellation.Token).ToArray());
    }

    [Fact]
    public void ThresholdValLengthIsBoundedAndScaleColorsKeepResolvedMetadata()
    {
        const string rule = "<conditionalFormatting sqref=\"A1\"><cfRule type=\"colorScale\" priority=\"1\"><colorScale><cfvo type=\"formula\" val=\"SUM(1,2,3,4,5)\"/><cfvo type=\"max\"/><color indexed=\"2\" tint=\"0.5\"/><color rgb=\"FFFFFFFF\"/></colorScale></cfRule></conditionalFormatting>";
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><v>1</v></c>") + rule, Fixture.Styles);
        using (var reader = OpenXmlWorkbookReader.Open(input))
        {
            var scale = Assert.Single(Assert.Single(reader.ReadWorksheetInfo(1).ConditionalFormattingRules).Source.Children);
            Assert.Equal("FF8080", scale.Children.Single(child => child.Name == "color" && child.Attributes.ContainsKey("indexed")).Attributes["resolvedRgb"]);
        }

        input.Position = 0;
        using var limited = OpenXmlWorkbookReader.Open(input, new() { MaxCellTextCharacters = 8 });
        Assert.Throws<InvalidDataException>(() => limited.ReadWorksheetInfo(1));
    }

    [Fact]
    public void ParsedCopyMatchesTextRewriterAndRetainsMixedReferencesAndLiterals()
    {
        const string expression = "AND($A2=B$3,$C$4=\"A2\")";
        var parsed = FormulaParser.Parse(expression).Formula!;
        var moved = FormulaRewriter.Copy(parsed, 5, 2);
        Assert.Equal(FormulaRewriter.Copy(expression, 5, 2), FormulaRewriter.Serialize(moved.Root));
        Assert.Same(parsed, FormulaRewriter.Copy(parsed, 0, 0));
    }

    [Fact]
    public void RuleAndFormulaLimitsRejectUnboundedMetadata()
    {
        using var input = Fixture.Create(Fixture.Cells("<c/>") + "<conditionalFormatting sqref=\"A1 B2\"><cfRule type=\"expression\" priority=\"1\"><formula>TRUE</formula></cfRule></conditionalFormatting>");
        using var reader = OpenXmlWorkbookReader.Open(input, new() { MaxStyleRecords = 1 });
        Assert.Throws<InvalidDataException>(() => reader.ReadWorksheetInfo(1));
    }
}
