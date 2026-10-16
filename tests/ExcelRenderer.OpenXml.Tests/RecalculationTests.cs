using ExcelRenderer.Formulas;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class RecalculationTests
{
    [Fact]
    public void SavesTypedCachesAndInvalidatesUnreachedCycle()
    {
        using var input = Fixture.Create(Fixture.Cells("""
            <c r="A1"><v>3</v></c><c r="B1"><f>A1*2</f><v>99</v></c>
            <c r="C1" t="str"><f>"日本語"</f><v>old</v></c>
            <c r="D1"><f>TRUE()</f><v>0</v></c><c r="E1"><f>1/0</f><v>42</v></c>
            <c r="F1"><f>F1</f><v>123</v></c>
            """));
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output, new[] { Target(2), Target(3), Target(4), Target(5) });
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        var cells = reader.EnumerateCells(1).ToArray();
        Assert.Equal(6, cells[1].Value.Number);
        Assert.Equal("日本語", cells[2].Value.Text);
        Assert.True(cells[3].Value.Boolean);
        Assert.Equal("#DIV/0!", cells[4].Value.Text);
        Assert.False(cells[5].Formula!.HasCachedResult);
        Assert.Null(cells[5].RawValue);
    }

    [Fact]
    public void EmptyStringFalseAndZeroRemainTypedCachedResults()
    {
        using var input = Fixture.Create(Fixture.Cells("""
            <c r="A1"><f>""</f><v>99</v></c>
            <c r="B1"><f>FALSE()</f><v>99</v></c>
            <c r="C1"><f>0</f><v>99</v></c>
            """));
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output);
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        var cells = reader.EnumerateCells(1).ToArray();
        Assert.All(cells, cell => Assert.True(cell.Formula!.HasCachedResult));
        Assert.Equal(CellValueType.String, cells[0].Value.Type);
        Assert.Equal(string.Empty, cells[0].Value.Text);
        Assert.Equal(CellValueType.Boolean, cells[1].Value.Type);
        Assert.False(cells[1].Value.Boolean);
        Assert.Equal(CellValueType.Number, cells[2].Value.Type);
        Assert.Equal(0, cells[2].Value.Number);
    }

    [Theory]
    [InlineData("<f>UNKNOWN()</f>")]
    [InlineData("<f t=\"array\" ref=\"A1:A2\">1</f>")]
    [InlineData("<f>A1</f>")]
    public void CalculationFailureDoesNotTouchDestination(string formula)
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\">" + formula + "<v>17</v></c>"));
        using var output = new MemoryStream();
        output.WriteByte(91);
        var exception = Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output));
        Assert.Contains("Sheet1!A1", exception.Message);
        Assert.Equal(new byte[] { 91 }, output.ToArray());
    }

    [Fact]
    public void SelectedReaderCanReachOtherSheetsAndFormatNewCache()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\" s=\"1\"><f>Second!A1*2</f></c>"), styles: Fixture.Styles, secondBody: Fixture.Cells("<c r=\"A1\"><v>12</v></c><c r=\"B1\"><f>B1</f></c>"));
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = new[] { "Sheet1" } });
        var session = WorkbookCalculationSession.Create(reader);
        var cell = session.EvaluateCell(reader.EnumerateCells(1).Single());
        Assert.Equal(24, cell.Value.Number);
        Assert.True(cell.Formula!.HasCachedResult);
        Assert.Equal("24.00", reader.FormatCell(cell).Text);
    }

    [Fact]
    public void RestoresSharedFormulaWithMixedReferences()
    {
        using var input = Fixture.Create("""
            <sheetData><row r="1"><c r="A1"><v>2</v></c><c r="B1"><f t="shared" si="0" ref="B1:B2">A1+$A$1</f><v>99</v></c></row>
            <row r="2"><c r="A2"><v>5</v></c><c r="B2"><f t="shared" si="0"/><v>99</v></c></row></sheetData>
            """);
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output);
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        Assert.Equal(new double?[] { 4, 7 }, reader.EnumerateCells(1).Where(cell => cell.Formula != null).Select(cell => cell.Value.Number));
    }

    [Fact]
    public void InvalidatesUnselectedArrayFollowersWithoutRemovingMetadata()
    {
        using var input = Fixture.Create("""
            <sheetData><row r="1"><c r="A1"><f>2+3</f><v>99</v></c><c r="B1"><f t="array" ref="B1:B2">UNKNOWN()</f><v>77</v></c></row>
            <row r="2"><c r="B2"><v>88</v></c></row></sheetData>
            """);
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output, new[] { Target(1) });
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        var cells = reader.EnumerateCells(1).ToArray();
        Assert.Equal(5, cells[0].Value.Number);
        foreach (var cell in cells.Skip(1))
        {
            Assert.Equal("array", cell.Formula!.Kind);
            Assert.False(cell.Formula.HasCachedResult);
            Assert.Null(cell.RawValue);
            Assert.NotEmpty(reader.FormatCell(cell).Diagnostics);
        }
    }

    [Fact]
    public void NamesRespectLocalPrecedenceAndExplicitSheetScope()
    {
        using var input = Fixture.Create(Fixture.Cells("""
            <c r="A1"><v>7</v></c><c r="B1"><f>Rate</f></c>
            <c r="C1"><f>Second!Rate</f></c><c r="D1"><f>Second!GlobalOnly</f></c>
            """), definitions: """
            <definedName name="Rate">Sheet1!$A$1</definedName>
            <definedName name="Rate" localSheetId="0">Second!$A$1</definedName>
            <definedName name="Rate" localSheetId="1">Second!$B$1</definedName>
            <definedName name="GlobalOnly">Sheet1!$A$1</definedName>
            """, secondBody: Fixture.Cells("<c r=\"A1\"><v>11</v></c><c r=\"B1\"><v>13</v></c>"));
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output);
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        var cells = reader.EnumerateCells(1).ToArray();
        Assert.Equal(11, cells[1].Value.Number);
        Assert.Equal(13, cells[2].Value.Number);
        Assert.Equal("#NAME?", cells[3].Value.Text);
    }

    [Fact]
    public void UnsupportedNamedFormulaIsNotCaughtByIfErrorOrReplacedByOldCache()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><f>IFERROR(Rate,99)</f><v>123</v></c>"), definitions: "<definedName name=\"Rate\">1+2</definedName>");
        using var output = new MemoryStream();
        var exception = Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output));
        Assert.Contains("Sheet1!A1", exception.Message);
        Assert.Contains("UnsupportedSyntax", exception.Message);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("cm")]
    [InlineData("vm")]
    public void RejectsReachedMetadataBackedFormulas(string attribute)
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\" " + attribute + "=\"1\"><f>1+2</f><v>17</v></c>"));
        using var output = new MemoryStream();
        var exception = Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output));
        Assert.Contains("Sheet1!A1", exception.Message);
        Assert.Contains("metadata", exception.Message);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void PreservesCircularDependencyDiagnosticsForApiAndCli()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><f>B1</f></c><c r=\"B1\"><f>A1</f></c>"));
        using var output = new MemoryStream();
        var exception = Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output));
        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("CircularReference", diagnostic.Code);
        Assert.Equal(new[] { Target(1), Target(2), Target(1) }, diagnostic.DependencyPath);
        Assert.Contains("Sheet1!A1 -> Sheet1!B1 -> Sheet1!A1", exception.Message);
    }

    [Fact]
    public void PreservesUnsupportedFunctionSpanAndFeature()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><f>1+UNKNOWN()</f></c>"));
        using var output = new MemoryStream();
        var exception = Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output));
        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("UnsupportedFunction", diagnostic.Code);
        Assert.Equal("UNKNOWN", diagnostic.Feature);
        Assert.Equal(Target(1), diagnostic.Cell);
        Assert.Equal(2, diagnostic.Span.Start);
        Assert.Equal(9, diagnostic.Span.Length);
        Assert.Contains("[feature UNKNOWN]", exception.Message);
        Assert.Contains("[span 2:9]", exception.Message);
    }

    [Fact]
    public void CancellationDoesNotTouchDestination()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><f>1+2</f></c>"));
        using var output = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => WorkbookRecalculator.Recalculate(input, output, cancellationToken: new CancellationToken(true)));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("ROUNDDOWN(0.29,2)", 0.29)]
    [InlineData("ROUNDUP(0.07,2)", 0.07)]
    [InlineData("ROUND(1.005,2)", 1.01)]
    [InlineData("ROUND(-1.005,2)", -1.01)]
    public void DecimalRoundingPersistsNumericCaches(string formula, double expected)
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><f>" + formula + "</f><v>99</v></c>"));
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output);
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        var cell = Assert.Single(reader.EnumerateCells(1));
        Assert.Equal(CellValueType.Number, cell.Value.Type);
        Assert.True(cell.Formula!.HasCachedResult);
        Assert.Equal(expected, cell.Value.Number);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StringLimitFailureLeavesDestinationUntouched(bool defaults, bool cacheBudget)
    {
        string text = new('x', defaults ? 16384 : 5);
        string body = "<c r=\"A1\" t=\"inlineStr\"><is><t>" + text + "</t></is></c><c r=\"B1\"><f>A1&amp;A1</f><v>99</v></c>";
        using var input = Fixture.Create(Fixture.Cells(body));
        using var output = new MemoryStream();
        output.WriteByte(91);
        var options = defaults ? null : new FormulaEvaluationOptions { MaxStringLength = cacheBudget ? 100 : 8, MaxCachedStringCharacters = cacheBudget ? 8 : 100 };
        var exception = Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output, options: options));
        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("ResourceLimitExceeded", diagnostic.Code);
        Assert.Equal(cacheBudget ? "MaxCachedStringCharacters" : "MaxStringLength", diagnostic.Feature);
        Assert.Equal(Target(2), diagnostic.Cell);
        Assert.Equal(new byte[] { 91 }, output.ToArray());
    }

    [Fact]
    public void SharedFormulaRecalculationPreservesQuotedSheetsAndSyntaxStrings()
    {
        using var input = Fixture.Create("""
            <sheetData><row r="1"><c r="A1"><v>12</v></c><c r="B1"><f t="shared" si="0" ref="B1:B2">IF('Second'!A1=0,"-","("&amp;A1&amp;")")</f><v>99</v></c></row>
            <row r="2"><c r="A2"><v>34</v></c><c r="B2"><f t="shared" si="0"/><v>99</v></c></row></sheetData>
            """, secondBody: """
            <sheetData><row r="1"><c r="A1"><v>0</v></c></row><row r="2"><c r="A2"><v>1</v></c></row></sheetData>
            """);
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output);
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        Assert.Equal(new[] { "-", "(34)" }, reader.EnumerateCells(1).Where(cell => cell.Formula != null).Select(cell => cell.Value.Text));
    }

    private static FormulaCellAddress Target(int column) => new FormulaCellAddress("Sheet1", 1, column);
}
