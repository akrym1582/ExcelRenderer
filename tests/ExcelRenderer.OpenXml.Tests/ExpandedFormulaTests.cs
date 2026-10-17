using ExcelRenderer.Formulas;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class ExpandedFormulaTests
{
    [Theory]
    [InlineData(false, 1462)]
    [InlineData(true, 0)]
    public void WorkbookDateSystemOverridesCallerWithoutMutatingOptions(bool date1904, double serial)
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><f>DATE(1904,1,1)</f></c>"), date1904: date1904);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var options = new FormulaEvaluationOptions { DateSystem = date1904 ? FormulaDateSystem.Excel1900 : FormulaDateSystem.Excel1904 };
        var original = options.DateSystem;
        var session = WorkbookCalculationSession.Create(reader, options);
        Assert.Equal(serial, session.EvaluateCell(reader.EnumerateCells(1).Single()).Value.Number);
        Assert.Equal(original, options.DateSystem);
    }

    [Theory]
    [InlineData(false, 1462.5)]
    [InlineData(true, 0.5)]
    public void IsoDatesPreserveTimeFraction(bool date1904, double expected)
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\" t=\"d\"><v>1904-01-01T12:00:00</v></c><c r=\"B1\"><f>A1</f></c>"), date1904: date1904);
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output);
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        Assert.Equal(expected, reader.EnumerateCells(1).Last().Value.Number);
    }

    [Theory]
    [InlineData("1904-01-01T12:00:00Z")]
    [InlineData("1904-01-01T12:00:00+09:00")]
    public void TimezoneIsoIsDiagnosedBeforeDestinationChanges(string iso)
    {
        using var input = Fixture.Create(Fixture.Cells($"<c r=\"A1\" t=\"d\"><v>{iso}</v></c><c r=\"B1\"><f>A1</f></c>"));
        using var output = new MemoryStream();
        output.WriteByte(7);
        var error = Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output));
        Assert.Contains("without timezone", error.Message);
        Assert.Equal(new byte[] { 7 }, output.ToArray());
    }

    [Fact]
    public void SparseSnapshotIncludesMetadataEvenWithoutValueOrFormula()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\" cm=\"1\"/><c r=\"B1\"><f>SUM(A:A)</f></c>"));
        using var output = new MemoryStream();
        Assert.Contains("metadata", Assert.Throws<FormulaCalculationException>(() => WorkbookRecalculator.Recalculate(input, output)).Message);
    }

    [Fact]
    public void SparseSnapshotIgnoresStyleOnlyUsedRangeAndIncludesColumnTail()
    {
        using var input = Fixture.Create("""
            <dimension ref="A1:XFD1048576"/><sheetData>
            <row r="1"><c r="B1"><f>SUM(A:A)</f></c><c r="C1"><f>COUNTIF(A:A,"")</f></c></row>
            <row r="1048576"><c r="A1048576"><v>7</v></c><c r="XFD1048576" s="0"/></row></sheetData>
            """);
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(input, output, options: new() { MaxCellVisits = 10 });
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        var cells = reader.EnumerateCells(1).ToArray();
        Assert.Equal(7, cells[0].Value.Number);
        Assert.Equal(1048575, cells[1].Value.Number);
    }
}
