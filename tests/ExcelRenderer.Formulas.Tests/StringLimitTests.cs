using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class StringLimitTests
{
    private static readonly FormulaCellAddress Origin = new("S", 1, 2);

    [Theory]
    [InlineData("\"12345678\"", true)]
    [InlineData("\"123456789\"", false)]
    [InlineData("\"1234\"&\"5678\"", true)]
    [InlineData("\"1234\"&\"56789\"", false)]
    [InlineData("IF(TRUE,\"123456789\",\"ok\")", false)]
    [InlineData("IF(TRUE,\"ok\",\"123456789\")", true)]
    [InlineData("IF(FALSE,\"123456789\",\"ok\")", true)]
    [InlineData("IFERROR(\"1234\"&\"56789\",0)", false)]
    public void SelectedTextValuesHonorSingleValueLimit(string formula, bool success)
    {
        var result = Session().Evaluate(FormulaParser.Parse(formula).Formula!, Origin);
        Assert.Equal(success, result.Success);
        if (!success) AssertLimit(result, "MaxStringLength", Origin);
    }

    [Fact]
    public void ConstantAndFormulaCellFailuresIdentifyTheFormulaAndSpan()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromText("sensitive"))
            .SetFormula(Origin, "IF(TRUE,A1,\"ok\")");
        var session = Session(builder.Build());
        var result = session.EvaluateCell(Origin);
        AssertLimit(result, "MaxStringLength", Origin);
        Assert.Equal(8, result.Diagnostics[0].Span.Start);
        Assert.Equal(2, result.Diagnostics[0].Span.Length);
        Assert.DoesNotContain("sensitive", result.Diagnostics[0].Message);
        Assert.False(session.EvaluatedCells[Origin].Success);
        AssertLimit(session.EvaluateCell(Origin), "MaxStringLength", Origin);
        AssertLimit(session.EvaluateCell(new("S", 1, 1)), "MaxStringLength", new("S", 1, 1));
    }

    [Fact]
    public void DoublingStopsAtTheOperatorAndLeavesNoPartialSuccess()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromText("x"));
        for (int row = 2; row <= 10; row++) builder.SetFormula(new("S", row, 1), $"A{row - 1}&A{row - 1}");
        var session = Session(builder.Build());
        var address = new FormulaCellAddress("S", 5, 1);
        var result = session.EvaluateCell(address);
        AssertLimit(result, "MaxStringLength", address);
        Assert.Equal(2, result.Diagnostics[0].Span.Start);
        Assert.Equal(1, result.Diagnostics[0].Span.Length);
        Assert.Equal("xxxxxxxx", session.EvaluateCell(new("S", 4, 1)).Value.Text);
        Assert.False(session.EvaluatedCells[address].Success);
        AssertLimit(session.EvaluateCell(address), "MaxStringLength", address);
        Assert.Equal("ok", session.Evaluate(FormulaParser.Parse("\"ok\"").Formula!, Origin).Value.Text);
    }

    [Fact]
    public void CacheCountsDistinctCellsOnceEvenForTheSameStringInstance()
    {
        string shared = new('x', 4);
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromText(shared))
            .SetValue(new("S", 2, 1), FormulaValue.FromText(shared))
            .SetFormula(new("S", 3, 1), "A1").Build();
        var options = new FormulaEvaluationOptions { MaxStringLength = 8, MaxCachedStringCharacters = 8 };
        var session = new FormulaEngine().CreateSession(source, options);
        for (int iteration = 0; iteration < 5; iteration++) Assert.Equal(shared, session.EvaluateCell(new("S", 1, 1)).Value.Text);
        Assert.Equal(shared, session.EvaluateCell(new("S", 2, 1)).Value.Text);
        var failure = session.EvaluateCell(new("S", 3, 1));
        AssertLimit(failure, "MaxCachedStringCharacters", new("S", 3, 1));
        Assert.Equal(0, failure.Diagnostics[0].Span.Start);
        Assert.Equal(2, failure.Diagnostics[0].Span.Length);
        Assert.False(session.EvaluatedCells[new("S", 3, 1)].Success);
    }

    [Fact]
    public void FormulaResultsAndDirectEvaluationsDoNotDoubleCountRetention()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetFormula(new("S", 1, 1), "\"1234\"")
            .SetFormula(new("S", 2, 1), "\"5678\"")
            .SetFormula(new("S", 3, 1), "\"9\"").Build();
        var session = new FormulaEngine().CreateSession(source, new() { MaxCachedStringCharacters = 8 });
        for (int iteration = 0; iteration < 5; iteration++)
        {
            Assert.True(session.Evaluate(FormulaParser.Parse("\"temporary\"").Formula!, Origin).Success);
            Assert.True(session.EvaluateCell(new("S", 1, 1)).Success);
        }
        Assert.True(session.EvaluateCell(new("S", 2, 1)).Success);
        AssertLimit(session.EvaluateCell(new("S", 3, 1)), "MaxCachedStringCharacters", new("S", 3, 1));
        Assert.Equal(2, session.EvaluatedCells.Values.Count(result => result.Success));
    }

    [Fact]
    public void CacheBudgetFailureCannotBeCaughtByIfError()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromText("x"))
            .SetFormula(Origin, "IFERROR(A1,0)").Build();
        var session = new FormulaEngine().CreateSession(source, new() { MaxCachedStringCharacters = 0 });
        AssertLimit(session.EvaluateCell(Origin), "MaxCachedStringCharacters", Origin);
    }

    [Fact]
    public void LimitsUseUtf16AndZeroAllowsOnlyEmptyText()
    {
        var session = new FormulaEngine().CreateSession(EmptySource(), new() { MaxStringLength = 2 });
        Assert.Equal("😀", session.Evaluate(FormulaParser.Parse("\"😀\"").Formula!, Origin).Value.Text);
        AssertLimit(session.Evaluate(FormulaParser.Parse("\"😀x\"").Formula!, Origin), "MaxStringLength", Origin);
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetValue(new("S", 1, 1), FormulaValue.FromText("")).Build();
        var zero = new FormulaEngine().CreateSession(source, new() { MaxStringLength = 0, MaxCachedStringCharacters = 0 });
        Assert.True(zero.EvaluateCell(new("S", 1, 1)).Success);
        Assert.True(zero.Evaluate(FormulaParser.Parse("\"\"&\"\"").Formula!, Origin).Success);
        AssertLimit(zero.Evaluate(FormulaParser.Parse("\"x\"").Formula!, Origin), "MaxStringLength", Origin);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void NegativeLimitsAreRejected(int single, long cached)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FormulaEngine().CreateSession(EmptySource(), new() { MaxStringLength = single, MaxCachedStringCharacters = cached }));
    }

    [Fact]
    public void OptionsAreCapturedAndCancellationAndStaticValidationRemainActive()
    {
        var options = new FormulaEvaluationOptions { MaxStringLength = 8, MaxCachedStringCharacters = 4 };
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetFormula(Origin, "\"12345\"").Build();
        var session = new FormulaEngine().CreateSession(source, options);
        options.MaxStringLength = 100;
        options.MaxCachedStringCharacters = 100;
        AssertLimit(session.Evaluate(FormulaParser.Parse("\"123456789\"").Formula!, Origin), "MaxStringLength", Origin);
        AssertLimit(session.EvaluateCell(Origin), "MaxCachedStringCharacters", Origin);
        var unsupported = Session().Evaluate(FormulaParser.Parse("IF(TRUE,\"ok\",UNKNOWN())").Formula!, Origin);
        Assert.Equal("UnsupportedFunction", unsupported.Diagnostics[0].Code);
        var cancelled = new FormulaEngine().CreateSession(EmptySource(), cancellationToken: new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(() => cancelled.Evaluate(FormulaParser.Parse("\"x\"&\"x\"").Formula!, Origin));
    }

    [Fact]
    public void RejectedConcatenationDoesNotAllocateTheCombinedString()
    {
        // An 8192-unit concatenation alone allocates over 16 KiB. Prepare the
        // 4096-unit operand, AST and cell cache before measuring a warmed failure.
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromText(new string('x', 4096))).Build();
        var session = new FormulaEngine().CreateSession(source, new() { MaxStringLength = 4096 });
        var parsed = FormulaParser.Parse("A1&A1").Formula!;
        Assert.True(session.EvaluateCell(new("S", 1, 1)).Success);
        Assert.False(session.Evaluate(parsed, Origin).Success);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = session.Evaluate(parsed, Origin);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertLimit(result, "MaxStringLength", Origin);
        Assert.True(allocated < 8192, $"Rejected concatenation allocated {allocated} bytes.");
    }

    private static void AssertLimit(FormulaEvaluationResult result, string limit, FormulaCellAddress cell)
    {
        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ResourceLimitExceeded", diagnostic.Code);
        Assert.Equal(limit, diagnostic.Feature);
        Assert.Contains(limit, diagnostic.Message);
        Assert.Equal(cell, diagnostic.Cell);
    }

    private static InMemoryFormulaWorkbook EmptySource() => new InMemoryFormulaWorkbook.Builder().AddSheet("S").Build();

    private static FormulaSession Session(IFormulaDataSource? source = null) => new FormulaEngine().CreateSession(source ?? EmptySource(), new() { MaxStringLength = 8 });
}
