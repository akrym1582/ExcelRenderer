using System.Globalization;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class ReviewRegressionTests
{
    public static IEnumerable<object[]> SyntaxCharacters => new[]
    {
        "", "+", "-", "*", "/", "^", "%", "&", "=", "<>", "<", "<=", ">", ">=", "(", ")", ",", ":", "!", "a\"b", "'O''Brien'", "#REF!",
    }.Select(text => new object[] { text });

    [Theory]
    [MemberData(nameof(SyntaxCharacters))]
    public void QuotedSyntaxIsText(string text)
    {
        string formula = "=  \"" + text.Replace("\"", "\"\"") + "\"";
        var parsed = FormulaParser.Parse(formula);
        Assert.True(parsed.Success, string.Join(";", parsed.Diagnostics));
        var node = parsed.Formula!.Root;
        Assert.Equal(FormulaNodeKind.Text, node.Kind);
        Assert.Equal(text, node.Text);
        Assert.Equal(3, node.Span.Start);
        Assert.Equal(formula.Length - 3, node.Span.Length);
        Assert.Empty(node.Children);
        Assert.Equal(text, Evaluate(formula).Value.Text);
    }

    [Theory]
    [InlineData("SUM(\",\")", FormulaValueKind.Error, "#VALUE!")]
    [InlineData("IF(TRUE,\"-\",\"\")", FormulaValueKind.Text, "-")]
    [InlineData("IF(FALSE,\"\",\",\")", FormulaValueKind.Text, ",")]
    [InlineData("\"(\"&A1&\")\"", FormulaValueKind.Text, "(12)")]
    public void QuotedArgumentsKeepTheirValue(string formula, FormulaValueKind kind, string expected)
    {
        var result = Evaluate(formula);
        Assert.True(result.Success);
        Assert.Equal(kind, result.Value.Kind);
        Assert.Equal(expected, result.Value.Text);
    }

    [Fact]
    public void DelimitersCannotCreateMissingArgumentsOrOperators()
    {
        var node = FormulaParser.Parse("=SUM(\",\")").Formula!.Root;
        var child = Assert.Single(node.Children);
        Assert.Equal(FormulaNodeKind.Text, child.Kind);
        Assert.Equal(",", child.Text);
        Assert.Equal(5, child.Span.Start);
        Assert.Equal(3, child.Span.Length);
        Assert.Equal(1, Evaluate("COUNTA(\",\")").Value.Number);
        Assert.False(FormulaParser.Parse("=1 \"+\" 2").Success);
        Assert.False(FormulaParser.Parse("=A1:\"B2\"").Success);
        Assert.False(FormulaParser.Parse("=\"A1\":B2").Success);
        Assert.False(FormulaParser.Parse("=A1 \"\"").Success);
        var missing = FormulaParser.Parse("=SUM(,1,)").Formula!.Root.Children;
        Assert.Equal(new[] { FormulaNodeKind.Missing, FormulaNodeKind.Number, FormulaNodeKind.Missing }, missing.Select(n => n.Kind));
        Assert.Equal(1, Evaluate("SUM(,1,)").Value.Number);
        Assert.Equal(12, Evaluate("='O''Brien'!A1").Value.Number);
        Assert.Equal("IF((B3=0),\"-\",B3)", FormulaRewriter.Copy("IF(A1=0,\"-\",A1)", 2, 1));
    }

    [Theory]
    [InlineData("ROUNDDOWN(0.29,2)", 0.29)]
    [InlineData("ROUNDUP(0.07,2)", 0.07)]
    [InlineData("ROUND(1.005,2)", 1.01)]
    [InlineData("ROUNDDOWN(-0.29,2)", -0.29)]
    [InlineData("ROUNDUP(-0.07,2)", -0.07)]
    [InlineData("ROUND(-1.005,2)", -1.01)]
    [InlineData("ROUND(-125,-1)", -130)]
    [InlineData("ROUND(1.005,2.9)", 1.01)]
    [InlineData("ROUND(125,-1.9)", 130)]
    [InlineData("ROUND(12,1E100)", 12)]
    [InlineData("ROUND(12,-1E100)", 0)]
    [InlineData("ROUNDDOWN(12,-1E100)", 0)]
    [InlineData("ROUNDUP(0,-1E100)", 0)]
    [InlineData("ROUND(5E-324,324)", double.Epsilon)]
    [InlineData("ROUND(5E-324,323)", 1E-323)]
    [InlineData("ROUNDDOWN(5E-324,323)", 0)]
    [InlineData("ROUNDUP(5E-324,323)", 1E-323)]
    [InlineData("ROUND(1.7976931348623157E308,-309)", 0)]
    [InlineData("ROUNDDOWN(1.7976931348623157E308,-308)", 1E308)]
    [InlineData("ROUND(1.7976931348623157E308,0)", double.MaxValue)]
    public void DecimalRoundingReproductions(string formula, double expected)
    {
        var result = Evaluate(formula);
        Assert.True(result.Success);
        Assert.Equal(FormulaValueKind.Number, result.Value.Kind);
        Assert.Equal(expected, result.Value.Number);
    }

    [Theory]
    [InlineData("ROUNDUP(12,-1E100)")]
    [InlineData("ROUNDUP(1.7976931348623157E308,-308)")]
    [InlineData("ROUND(1.7976931348623157E308,-308)")]
    public void RoundingOverflowIsNumericError(string formula)
    {
        var result = Evaluate(formula);
        Assert.True(result.Success);
        Assert.Equal(FormulaValueKind.Error, result.Value.Kind);
        Assert.Equal("#NUM!", result.Value.Error);
    }

    [Fact]
    public void AdjacentDoublesUseShortestDecimalProfileForLiteralsAndReferences()
    {
        // Expectations follow decimal strings 1.0049999999999997, 1.005,
        // and 1.0050000000000001, independently of the rounding helper.
        foreach (var (number, expected) in new[] { (Math.BitDecrement(1.005), 1.00), (1.005, 1.01), (Math.BitIncrement(1.005), 1.01) })
        {
            foreach (int sign in new[] { -1, 1 })
            {
                var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetValue(new("S", 1, 1), FormulaValue.FromNumber(sign * number)).Build();
                string literal = (sign * number).ToString("R", CultureInfo.InvariantCulture);
                Assert.Equal(sign * expected, Evaluate($"ROUND({literal},2)").Value.Number);
                Assert.Equal(sign * expected, new FormulaEngine().Evaluate(FormulaParser.Parse("ROUND(A1,2)").Formula!, source, new("S", 1, 2)).Value.Number);
            }
        }
        Assert.Equal(0.300000000000001, Evaluate("ROUNDUP(0.1+0.2,15)").Value.Number);
    }

    [Fact]
    public void DecimalValuesRequiringNoRoundingArePreserved()
    {
        foreach (string mode in new[] { "ROUND", "ROUNDUP", "ROUNDDOWN" })
        foreach (double number in new[] { -125, -1.29, -0.29, -0.07, 0, 0.07, 0.29, 1.21, 125 })
        {
            string literal = number.ToString("R", CultureInfo.InvariantCulture);
            Assert.Equal(number, Evaluate($"{mode}({literal},2)").Value.Number);
        }
    }

    [Fact]
    public void SmallDecimalGridMatchesIndependentIntegerModel()
    {
        foreach (string mode in new[] { "ROUND", "ROUNDUP", "ROUNDDOWN" })
        for (int coefficient = -201; coefficient <= 201; coefficient++)
        {
            // Input coefficient/100 is rounded to tenths using integer arithmetic.
            int magnitude = Math.Abs(coefficient);
            int quotient = magnitude / 10;
            int remainder = magnitude % 10;
            if ((mode == "ROUND" && remainder >= 5) || (mode == "ROUNDUP" && remainder != 0)) quotient++;
            double expected = Math.Sign(coefficient) * quotient / 10d;
            string literal = (coefficient / 100d).ToString("R", CultureInfo.InvariantCulture);
            Assert.Equal(expected, Evaluate($"{mode}({literal},1)").Value.Number);
        }
    }

    [Theory]
    [InlineData("ROUNDDOWN", 0.29, 0.29)]
    [InlineData("ROUNDUP", 0.07, 0.07)]
    [InlineData("ROUND", 1.005, 1.01)]
    [InlineData("ROUNDDOWN", -0.29, -0.29)]
    [InlineData("ROUNDUP", -0.07, -0.07)]
    [InlineData("ROUND", -1.005, -1.01)]
    public void ReferenceInputsMatchDecimalLiteralInputs(string mode, double number, double expected)
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromNumber(number))
            .SetValue(new("S", 1, 2), FormulaValue.FromNumber(2)).Build();
        var result = new FormulaEngine().Evaluate(FormulaParser.Parse(mode + "(A1,B1)").Formula!, source, new("S", 1, 3));
        Assert.True(result.Success);
        Assert.Equal(FormulaValueKind.Number, result.Value.Kind);
        Assert.Equal(expected, result.Value.Number);
    }

    [Theory]
    [InlineData("ROUND(-0.01,0)", true)]
    [InlineData("ROUNDDOWN(-0.01,0)", true)]
    [InlineData("ROUND(-1,-1)", true)]
    [InlineData("ROUND(-1,-10000)", false)]
    [InlineData("ROUND(-0,0)", true)]
    public void RoundingPreservesExistingSignedZeroConvention(string formula, bool negative)
    {
        var result = Evaluate(formula);
        Assert.True(result.Success);
        Assert.Equal(FormulaValueKind.Number, result.Value.Kind);
        Assert.Equal(negative ? long.MinValue : 0L, BitConverter.DoubleToInt64Bits(result.Value.Number));
    }

    private static FormulaEvaluationResult Evaluate(string formula)
    {
        var parsed = FormulaParser.Parse(formula);
        Assert.True(parsed.Success, string.Join(";", parsed.Diagnostics));
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").AddSheet("O'Brien")
            .SetValue(new("S", 1, 1), FormulaValue.FromNumber(12))
            .SetValue(new("O'Brien", 1, 1), FormulaValue.FromNumber(12)).Build();
        return new FormulaEngine().Evaluate(parsed.Formula!, source, new("S", 1, 2));
    }
}
