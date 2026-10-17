using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class EvaluationTests
{
    private static readonly FormulaCellAddress Origin = new("Sheet1", 1, 1);

    [Theory]
    [InlineData("=1+2*3", 7)]
    [InlineData("=(1+2)*3", 9)]
    [InlineData("=-2^2", 4)]
    [InlineData("=2^3^2", 64)]
    [InlineData("=2^-2", 0.25)]
    [InlineData("=50%*200", 100)]
    [InlineData("=IF(TRUE,1,1/0)", 1)]
    [InlineData("=IF(FALSE,1/0,2)", 2)]
    [InlineData("=IFERROR(1/0,99)", 99)]
    [InlineData("=ROUND(2.5,0)", 3)]
    [InlineData("=ROUND(-2.5,0)", -3)]
    [InlineData("=ROUND(125,-1)", 130)]
    [InlineData("=ROUNDUP(-1.21,1)", -1.3)]
    [InlineData("=ROUNDDOWN(-1.29,1)", -1.2)]
    [InlineData("=INT(-1.2)", -2)]
    [InlineData("=MOD(-3,2)", 1)]
    [InlineData("=MOD(3,-2)", -1)]
    [InlineData("=ABS(-3)", 3)]
    [InlineData("=SUM(\"2\",TRUE,3)", 6)]
    [InlineData("=SUM()", 0)]
    [InlineData("=COUNT()", 0)]
    [InlineData("=COUNTA()", 0)]
    [InlineData("=IF(FALSE,1,)", 0)]
    [InlineData("=ROUND(1E-310,309)", 0)]
    [InlineData("=ROUNDUP(1E-310,309)", 1E-309)]
    [InlineData("=ROUNDUP(-1E-310,309)", -1E-309)]
    [InlineData("=ROUNDUP(5E-324,-308)", 1E308)]
    [InlineData("=ROUNDUP(-5E-324,-308)", -1E308)]
    [InlineData("=ROUNDDOWN(1E-310,309)", 0)]
    [InlineData("=ROUND(12,10000)", 12)]
    [InlineData("=ROUND(12,-10000)", 0)]
    public void HandCalculatedNumbers(string formula, double expected)
    {
        var result = Evaluate(formula);
        Assert.True(result.Success, string.Join(";", result.Diagnostics));
        Assert.Equal(FormulaValueKind.Number, result.Value.Kind);
        Assert.Equal(expected, result.Value.Number);
    }

    [Theory]
    [InlineData("=AND(FALSE,1/0)", "#DIV/0!")]
    [InlineData("=OR(TRUE,1/0)", "#DIV/0!")]
    [InlineData("=AVERAGE(B1:B3)", "#DIV/0!")]
    [InlineData("=0^0", "#NUM!")]
    [InlineData("=\"invalid\"+(1/0)", "#VALUE!")]
    [InlineData("=0^-1", "#DIV/0!")]
    [InlineData("=(-1)^0.5", "#NUM!")]
    [InlineData("=1+\"1,000\"", "#VALUE!")]
    [InlineData("=Missing!A1", "#REF!")]
    [InlineData("=UnknownName", "#NAME?")]
    public void ExcelErrorsAreValues(string formula, string expected)
    {
        var result = Evaluate(formula);
        Assert.True(result.Success);
        Assert.Equal(FormulaValueKind.Error, result.Value.Kind);
        Assert.Equal(expected, result.Value.Error);
    }

    [Theory]
    [InlineData("=IFERROR(UNKNOWN(),99)", "UnsupportedFunction")]
    [InlineData("=IF(TRUE,1,UNKNOWN())", "UnsupportedFunction")]
    [InlineData("=IF(TRUE,1,ABS())", "InvalidArgumentCount")]
    [InlineData("=A1:A2", "UnsupportedArrayEvaluation")]
    [InlineData("=SUM(IFERROR(A1:A2,0))", "UnsupportedArrayEvaluation")]
    public void EngineFailuresCannotBeHidden(string formula, string code)
    {
        var result = Evaluate(formula);
        Assert.False(result.Success);
        Assert.Equal(code, result.Diagnostics[0].Code);
    }

    [Fact]
    public void ReferenceCoercionAndBlankProfile()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Sheet1");
        builder.SetValue(new("Sheet1", 1, 1), FormulaValue.FromText("2"));
        builder.SetValue(new("Sheet1", 2, 1), FormulaValue.FromBoolean(true));
        builder.SetValue(new("Sheet1", 3, 1), FormulaValue.FromNumber(3));
        builder.SetValue(new("Sheet1", 2, 2), FormulaValue.FromText(""));
        builder.SetValue(new("Sheet1", 3, 2), FormulaValue.FromNumber(0));
        var data = builder.Build();
        Assert.Equal(3, Evaluate("SUM(A1:A3)", data).Value.Number);
        Assert.Equal(2, Evaluate("COUNTA(B1:B3)", data).Value.Number);
        Assert.Equal(0, Evaluate("AVERAGE(B1:B3)", data).Value.Number);
        Assert.Equal(3, Evaluate("SUM(IF(TRUE,A1:A3,B1:B3))", data).Value.Number);
        Assert.Equal(FormulaValueKind.Boolean, Evaluate("IF(FALSE,1)", data).Value.Kind);
        Assert.False(Evaluate("IF(FALSE,1)", data).Value.Boolean);
        Assert.Equal("売上合計", Evaluate("\"売上\"&\"合計\"", data).Value.Text);
    }

    [Fact]
    public void FormulaReferencingBlankProducesNumericZeroForDependents()
    {
        var data = new InMemoryFormulaWorkbook.Builder().AddSheet("Sheet1")
            .SetFormula(new("Sheet1", 2, 1), "A1").Build();
        var session = new FormulaEngine().CreateSession(data);
        Assert.Equal(FormulaValueKind.Number, session.EvaluateCell(new("Sheet1", 2, 1)).Value.Kind);
        var result = session.Evaluate(FormulaParser.Parse("COUNT(A1:A2)").Formula!, Origin);
        Assert.Equal(1, result.Value.Number);
        Assert.Equal(1, Evaluate("COUNTA(A1:A2)", data).Value.Number);
    }

    [Fact]
    public void SessionSharesDiamondAndIgnoresUnreachableCycle()
    {
        var data = new CountingSource();
        var session = new FormulaEngine().CreateSession(data);
        Assert.Equal(6, session.EvaluateCell(new("S", 1, 4)).Value.Number);
        Assert.Equal(4, session.EvaluateCell(new("S", 1, 3)).Value.Number);
        Assert.Equal(1, data.Reads[new("S", 1, 1)]);
        Assert.Equal(3, session.EvaluatedCells.Count);
        var cycle = session.EvaluateCell(new("S", 2, 1));
        Assert.Equal("CircularReference", cycle.Diagnostics[0].Code);
        Assert.True(cycle.Diagnostics[0].DependencyPath.Count >= 2);
    }

    [Fact]
    public void CancellationAndResourceLimits()
    {
        var data = new InMemoryFormulaWorkbook.Builder().AddSheet("Sheet1").Build();
        var parsed = FormulaParser.Parse("SUM(A:A)").Formula!;
        var result = new FormulaEngine().Evaluate(parsed, new DenseFormulaSource(data), Origin, new() { MaxCellVisits = 100 });
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new FormulaEngine().Evaluate(parsed, data, Origin, cancellationToken: cancellation.Token));
        Assert.Equal(7, Evaluate("1+6", data).Value.Number);
    }

    [Fact]
    public void InvoiceSampleWithNamesAndCrossSheetTax()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Invoice").AddSheet("Tax");
        builder.SetValue(new("Tax", 1, 1), FormulaValue.FromNumber(0.1));
        builder.AddName("税率", new("Tax", 1, 1, 1, 1));
        for (int row = 1; row <= 3; row++)
        {
            builder.SetValue(new("Invoice", row, 1), FormulaValue.FromNumber(row));
            builder.SetValue(new("Invoice", row, 2), FormulaValue.FromNumber(125));
            builder.SetFormula(new("Invoice", row, 3), $"ROUND(A{row}*B{row}*(1+税率),0)");
        }
        builder.SetFormula(new("Invoice", 4, 3), "SUM(C1:C3)");
        Assert.Equal(826, new FormulaEngine().EvaluateCell(new("Invoice", 4, 3), builder.Build()).Value.Number);
    }

    [Fact]
    public void SnapshotsAndNamesAreIsolated()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Sheet1").AddSheet("Other");
        builder.SetValue(new("Sheet1", 1, 1), FormulaValue.FromNumber(5));
        builder.SetValue(new("Other", 1, 1), FormulaValue.FromNumber(9));
        builder.AddName("価格", new("Sheet1", 1, 1, 1, 1));
        builder.AddName("価格", new("Other", 1, 1, 1, 1), "Sheet1");
        var first = builder.Build();
        builder.SetValue(new("Sheet1", 2, 1), FormulaValue.FromNumber(7));
        Assert.Equal(0, Evaluate("A2", first).Value.Number);
        Assert.Equal(7, Evaluate("A2", builder.Build()).Value.Number);
        Assert.Equal(9, Evaluate("価格", first).Value.Number);
        Assert.Equal(9, Evaluate("Sheet1!価格", first).Value.Number);
        Assert.Throws<ArgumentException>(() => builder.AddSheet("sheet1"));
    }

    private static FormulaEvaluationResult Evaluate(string formula, IFormulaDataSource? data = null)
    {
        var parsed = FormulaParser.Parse(formula);
        Assert.True(parsed.Success, string.Join(";", parsed.Diagnostics));
        return new FormulaEngine().Evaluate(parsed.Formula!, data ?? new InMemoryFormulaWorkbook.Builder().AddSheet("Sheet1").Build(), Origin);
    }

    private sealed class CountingSource : IFormulaDataSource
    {
        public Dictionary<FormulaCellAddress, int> Reads { get; } = new();
        public FormulaCellData GetCell(FormulaCellAddress address)
        {
            Reads[address] = Reads.GetValueOrDefault(address) + 1;
            if (address.Row == 2) return FormulaCellData.FromFormula("A2");
            return address.Column switch { 1 => FormulaCellData.FromValue(FormulaValue.FromNumber(1)), 2 => FormulaCellData.FromFormula("A1+1"), 3 => FormulaCellData.FromFormula("B1+2"), _ => FormulaCellData.FromFormula("B1+C1") };
        }
        public bool TryResolveSheet(string name, out string id) { id = name; return true; }
        public bool TryResolveName(string? sheet, string name, string current, out FormulaRange range) { range = default; return false; }
    }
}
