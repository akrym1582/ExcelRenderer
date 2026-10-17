using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class ExpandedFunctionLimitsTests
{
    [Theory]
    [InlineData("SUBSTITUTE(A1,\"x\",A1)")]
    [InlineData("CONCATENATE(A1,A1)")]
    [InlineData("TEXT(1,\"000000\")")]
    public void NewTextOperationsCheckOutputLengthBeforeAllocation(string formula)
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetValue(new("S", 1, 1), FormulaValue.FromText("xxxx")).Build();
        var result = new FormulaEngine().Evaluate(FormulaParser.Parse(formula).Formula!, source, new("S", 1, 2), new() { MaxStringLength = 5 });
        Assert.False(result.Success);
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
        Assert.Equal("MaxStringLength", result.Diagnostics[0].Feature);
    }

    [Fact]
    public void NewTextFormulaResultsUseCumulativeCacheBudget()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetFormula(new("S", 1, 1), "LEFT(\"abcdefgh\",4)")
            .SetFormula(new("S", 2, 1), "RIGHT(\"abcdefgh\",4)").Build();
        var session = new FormulaEngine().CreateSession(source, new() { MaxCachedStringCharacters = 7 });
        Assert.True(session.EvaluateCell(new("S", 1, 1)).Success);
        Assert.Equal("ResourceLimitExceeded", session.EvaluateCell(new("S", 2, 1)).Diagnostics[0].Code);
    }

    [Fact]
    public void FormattingUsesExactOutputLengthAndDateOptionsAreCopied()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").Build();
        var options = new FormulaEvaluationOptions { DateSystem = FormulaDateSystem.Excel1904, MaxStringLength = 9 };
        var session = new FormulaEngine().CreateSession(source, options);
        options.DateSystem = FormulaDateSystem.Excel1900;
        var result = session.Evaluate(FormulaParser.Parse("TEXT(0,\"yyyy年m月d日\")").Formula!, new("S", 1, 1));
        Assert.True(result.Success);
        Assert.Equal("1904年1月1日", result.Value.Text);
        session = new FormulaEngine().CreateSession(source, new() { MaxStringLength = 9 });
        Assert.Equal("1900年1月0日", session.Evaluate(FormulaParser.Parse("TEXT(0,\"yyyy年m月d日\")").Formula!, new("S", 1, 1)).Value.Text);
    }

    [Theory]
    [InlineData("INDEX(A1:B1,,2)", "b")]
    [InlineData("INDEX(A1:A2,2,)", "c")]
    public void OneDimensionalIndexAllowsAppropriateOmission(string formula, string expected)
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 2), FormulaValue.FromText("b"))
            .SetValue(new("S", 2, 1), FormulaValue.FromText("c")).Build();
        Assert.Equal(expected, new FormulaEngine().Evaluate(FormulaParser.Parse(formula).Formula!, source, new("S", 3, 3)).Value.Text);
    }

    [Fact]
    public void LookupDoesNotReadNonmatchingReturnCells()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromNumber(1))
            .SetFormula(new("S", 1, 2), "UNKNOWN()")
            .SetValue(new("S", 2, 1), FormulaValue.FromNumber(2))
            .SetValue(new("S", 2, 2), FormulaValue.FromNumber(42)).Build();
        var result = new FormulaEngine().Evaluate(FormulaParser.Parse("VLOOKUP(2,A1:B2,2,FALSE)").Formula!, source, new("S", 3, 3));
        Assert.True(result.Success);
        Assert.Equal(42, result.Value.Number);
    }

    [Fact]
    public void WildcardWorkIsBoundedAndCannotBeCaughtByIferror()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetValue(new("S", 1, 1), FormulaValue.FromText(new string('a', 1000))).Build();
        var parsed = FormulaParser.Parse("IFERROR(COUNTIF(A1,\"*aaaaaaaaab\"),99)").Formula!;
        var result = new FormulaEngine().Evaluate(parsed, source, new("S", 1, 2), new() { MaxEvaluationSteps = 100 });
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
    }

    [Theory]
    [InlineData("ſ", "s")]
    [InlineData("ı", "i")]
    [InlineData("é", "É")]
    public void WildcardLiteralsFollowOrdinalIgnoreCase(string candidate, string criterion)
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetValue(new("S", 1, 1), FormulaValue.FromText(candidate)).Build();
        var result = new FormulaEngine().Evaluate(FormulaParser.Parse($"COUNTIF(A1,\"{criterion}\")").Formula!, source, new("S", 1, 2));
        Assert.Equal(StringComparer.OrdinalIgnoreCase.Equals(candidate, criterion) ? 1 : 0, result.Value.Number);
    }

    [Fact]
    public void MaximumConditionalArgumentPairsAreAcceptedAndOddCountsAreRejected()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").Build();
        foreach (var name in new[] { "COUNTIFS", "SUMIFS" })
        {
            var formula = name + "(" + (name == "SUMIFS" ? "B1," : "") + string.Join(",", Enumerable.Repeat("A1,0", 127)) + ")";
            var result = new FormulaEngine().Evaluate(FormulaParser.Parse(formula).Formula!, source, new("S", 1, 3));
            Assert.True(result.Success);
            Assert.Equal(name == "SUMIFS" ? 0 : 1, result.Value.Number);
        }
        var invalid = new FormulaEngine().Evaluate(FormulaParser.Parse("COUNTIFS(A1,0,B1)").Formula!, source, new("S", 1, 3));
        Assert.Equal("InvalidArgumentCount", invalid.Diagnostics[0].Code);
    }
}
