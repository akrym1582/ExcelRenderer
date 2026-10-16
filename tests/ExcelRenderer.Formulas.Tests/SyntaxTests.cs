using System;
using System.Linq;
using ExcelRenderer.Formulas;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class SyntaxTests
{
    [Theory]
    [InlineData("=1+2*3")]
    [InlineData("=-2^2")]
    [InlineData("=2^-2")]
    [InlineData("=2^3^2")]
    [InlineData("SUM(,1,)")]
    [InlineData("IF(FALSE,1,)")]
    [InlineData("'O''Brien'!$A1:$B$4")]
    [InlineData("'売上 明細'!A1")]
    [InlineData("SUM(A:A,$A:$C,1:10,$1:$10)")]
    [InlineData("Sheet1!1:10")]
    [InlineData("Sheet1!売上範囲")]
    [InlineData("LOG10(1)")]
    [InlineData("\"\"")]
    [InlineData("\"日本語\"&\"a\"\"b\"")]
    [InlineData("1.25E-3")]
    public void ParsesAndRoundTrips(string formula)
    {
        var parsed = FormulaParser.Parse(formula);
        Assert.True(parsed.Success, string.Join(";", parsed.Diagnostics));
        Assert.True(FormulaParser.Parse(FormulaRewriter.Serialize(parsed.Formula!.Root)).Success);
    }

    [Theory]
    [InlineData("{1,2}")]
    [InlineData("[Book.xlsx]Sheet1!A1")]
    [InlineData("Table1[Amount]")]
    [InlineData("Sheet1:Sheet3!A1")]
    [InlineData("@A1")]
    [InlineData("A1#")]
    [InlineData("A1 B1")]
    [InlineData("(A1,B1)")]
    [InlineData("SUM(1")]
    [InlineData("\"unclosed")]
    [InlineData("A0")]
    [InlineData("A$")]
    [InlineData("$Named")]
    [InlineData("Sheet1!42")]
    [InlineData("XFE1")]
    [InlineData("A1048577")]
    [InlineData("1.2.3")]
    public void RejectsUnsupportedOrInvalid(string formula)
    {
        Assert.False(FormulaParser.Parse(formula).Success);
    }

    [Fact]
    public void PreservesFlagsAndSourcePositions()
    {
        var node = FormulaParser.Parse("=  $A$1").Formula!.Root;
        Assert.Equal(3, node.Span.Start);
        Assert.Equal(4, node.Span.Length);
        Assert.True(node.AbsoluteRow);
        Assert.True(node.AbsoluteColumn);
    }

    [Fact]
    public void CopyDistinguishesAbsoluteAndRelativeReferencesAndLiterals()
    {
        Assert.Equal("SUM(B3,$B$2,D$3,$D6,\"A1\")", FormulaRewriter.Copy("SUM(A1,$B$2,C$3,$D4,\"A1\")", 2, 1));
        Assert.Equal("#REF!", FormulaRewriter.Copy("A1", -1, 0));
    }

    [Fact]
    public void InsertMovesAbsoluteReferencesAndOnlyTargetSheet()
    {
        string result = FormulaRewriter.InsertRows("SUM(A1:A5,$B$4,'Other'!A4,\"A4\")", "Sheet", "Sheet", 3, 2);
        Assert.Equal("SUM(A1:A7,$B$6,'Other'!A4,\"A4\")", result);
    }

    [Fact]
    public void ResourceLimitsAreDiagnostics()
    {
        Assert.Equal("ResourceLimitExceeded", FormulaParser.Parse(new string('(', 130) + "1" + new string(')', 130)).Diagnostics[0].Code);
        Assert.Equal("ResourceLimitExceeded", FormulaParser.Parse(new string('1', 8193)).Diagnostics[0].Code);
        Assert.Equal("ResourceLimitExceeded", FormulaParser.Parse(string.Join("+", Enumerable.Repeat("1", 3000))).Diagnostics[0].Code);
    }

    [Fact]
    public void FixedSeedInvalidInputsNeverThrow()
    {
        var random = new Random(137);
        const string alphabet = "AZ09()+-*/%$!:@#[],;\"' abc日本";
        for (int example = 0; example < 2000; example++)
        {
            var chars = new char[random.Next(1, 80)];
            for (int index = 0; index < chars.Length; index++)
            {
                chars[index] = alphabet[random.Next(alphabet.Length)];
            }

            FormulaParser.Parse(new string(chars));
        }
    }
}
