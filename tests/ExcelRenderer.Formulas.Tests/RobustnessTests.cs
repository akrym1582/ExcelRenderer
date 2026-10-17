using System.Text.Json;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class RobustnessTests
{
    [Theory]
    [InlineData(2, 100, 100)]
    [InlineData(100, 2, 100)]
    [InlineData(100, 100, 2)]
    public void ReusedAstHonorsSessionLimits(int length, int nodes, int depth)
    {
        var formula = FormulaParser.Parse("((1+2)*3)").Formula!;
        var data = new InMemoryFormulaWorkbook.Builder().AddSheet("S").Build();
        var options = new FormulaEvaluationOptions { MaxFormulaLength = length, MaxAstNodes = nodes, MaxSyntaxDepth = depth };
        var session = new FormulaEngine().CreateSession(data, options);
        options.MaxFormulaLength = 100; options.MaxAstNodes = 100; options.MaxSyntaxDepth = 100;
        var result = session.Evaluate(formula, new("S", 1, 1));
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
    }

    [Fact]
    public void RecordedAcceptanceFixturesHaveIndependentProvenance()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "acceptance.json")));
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").Build();
        foreach (var fixture in document.RootElement.EnumerateArray())
        {
            Assert.Contains(fixture.GetProperty("expectedSource").GetString(), new[] { "specification", "hand-calculated", "v1-profile" });
            Assert.NotEmpty(fixture.GetProperty("basis").GetString()!);
            var parsed = FormulaParser.Parse(fixture.GetProperty("formula").GetString()!);
            Assert.True(parsed.Success);
            var result = new FormulaEngine().Evaluate(parsed.Formula!, source, new("S", 1, 1));
            Assert.True(result.Success);
            Assert.Equal(fixture.GetProperty("kind").GetString(), result.Value.Kind.ToString());
            switch (result.Value.Kind)
            {
                case FormulaValueKind.Number: Assert.Equal(fixture.GetProperty("number").GetDouble(), result.Value.Number); break;
                case FormulaValueKind.Boolean: Assert.Equal(fixture.GetProperty("boolean").GetBoolean(), result.Value.Boolean); break;
                default: Assert.Equal(fixture.GetProperty("text").GetString(), result.Value.Text); break;
            }
        }
    }

    [Fact]
    public void SeededMalformedInputDoesNotEscapeOrHang()
    {
        const string chars = "AB12$!:+-*^%()[],\"'#@ あいう";
        var random = new Random(1582);
        for (int iteration = 0; iteration < 2000; iteration++)
        {
            var input = new string(Enumerable.Range(0, random.Next(0, 100)).Select(_ => chars[random.Next(chars.Length)]).ToArray());
            var result = FormulaParser.Parse(input);
            Assert.True(result.Success || result.Diagnostics.Count > 0);
            if (result.Success) Assert.Equal(input, result.Formula!.Text);
        }
    }

    [Fact]
    public void DependenciesAndFormulaCountsHaveDeterministicLimits()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int row = 1; row <= 100; row++) builder.SetFormula(new("S", row, 1), $"A{row + 1}+1");
        var result = new FormulaEngine().EvaluateCell(new("S", 1, 1), builder.Build(), new() { MaxDependencyDepth = 12 });
        Assert.False(result.Success);
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
        Assert.Equal(12, result.Diagnostics[0].DependencyPath.Count);
        var session = new FormulaEngine().CreateSession(builder.Build(), new() { MaxFormulaCells = 1 });
        Assert.Equal("ResourceLimitExceeded", session.EvaluateCell(new("S", 1, 1)).Diagnostics[0].Code);
    }

    [Fact]
    public void UnselectedCycleAndErrorCountingStayDistinct()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        builder.SetFormula(new("S", 1, 1), "IF(TRUE,1,A1)");
        builder.SetValue(new("S", 2, 1), FormulaValue.FromError("#N/A"));
        builder.SetFormula(new("S", 3, 1), "COUNT(A2)");
        builder.SetFormula(new("S", 4, 1), "COUNTA(A2)");
        var session = new FormulaEngine().CreateSession(builder.Build());
        Assert.Equal(1, session.EvaluateCell(new("S", 1, 1)).Value.Number);
        Assert.Equal(0, session.EvaluateCell(new("S", 3, 1)).Value.Number);
        Assert.Equal(1, session.EvaluateCell(new("S", 4, 1)).Value.Number);
    }

    [Fact]
    public void FiniteValuesAndBuilderDuplicatesAreEnforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FormulaValue.FromNumber(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormulaValue.FromNumber(double.PositiveInfinity));
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetValue(new("S", 1, 1), FormulaValue.FromNumber(1));
        Assert.Throws<ArgumentException>(() => builder.SetFormula(new("S", 1, 1), "2"));
        Assert.Throws<ArgumentException>(() => builder.AddName("bad name", new("S", 1, 1, 1, 1)));
    }
}
