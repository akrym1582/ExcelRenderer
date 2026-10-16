using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class DataSourceContractTests
{
    [Theory]
    [InlineData("R1C1")]
    [InlineData("r123c45")]
    [InlineData("Sheet1!R1C1")]
    [InlineData("SUM(R1C1)")]
    public void R1C1IsExplicitlyUnsupported(string text)
    {
        var parsed = FormulaParser.Parse(text);
        Assert.False(parsed.Success);
        Assert.Equal("UnsupportedSyntax", parsed.Diagnostics[0].Code);
        Assert.Equal("R1C1", parsed.Diagnostics[0].Feature);
    }

    [Theory]
    [InlineData("RC1")]
    [InlineData("R1C1Total")]
    [InlineData("R1C1!A1")]
    [InlineData("R1C1(1)")]
    public void A1AddressesNamesSheetsAndFunctionsStayUnambiguous(string text)
    {
        Assert.True(FormulaParser.Parse(text).Success);
    }

    [Fact]
    public void MissingSheetDiffersFromEmptyCell()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("Present").Build();
        var session = new FormulaEngine().CreateSession(source);
        Assert.Equal(0, session.EvaluateCell(new("Present", 1, 1)).Value.Number);
        var missing = session.EvaluateCell(new("Absent", 1, 1));
        Assert.False(missing.Success);
        Assert.Equal("DataSourceFailure", missing.Diagnostics[0].Code);
        var qualified = session.Evaluate(FormulaParser.Parse("Absent!A1").Formula!, new("Present", 1, 1));
        Assert.Equal("#REF!", qualified.Value.Error);
    }

    [Theory]
    [InlineData("GetCell", "A1")]
    [InlineData("TryResolveSheet", "Other!A1")]
    [InlineData("TryResolveName", "NamedValue")]
    public void DataSourceExceptionsAreDiagnostics(string failingMethod, string formula)
    {
        var result = new FormulaEngine().Evaluate(FormulaParser.Parse(formula).Formula!, new ThrowingSource(failingMethod), new("S", 1, 1));
        Assert.False(result.Success);
        Assert.Equal("DataSourceFailure", result.Diagnostics[0].Code);
        Assert.Contains(failingMethod, result.Diagnostics[0].Message);
    }

    [Fact]
    public void StableIdentifiersNeedNotBeDisplayNames()
    {
        var source = new OpaqueSource();
        var session = new FormulaEngine().CreateSession(source);
        Assert.Equal(42, session.EvaluateCell(new("opaque-id", 1, 1)).Value.Number);
        Assert.Equal(42, session.Evaluate(FormulaParser.Parse("Visible!A1").Formula!, new("opaque-id", 1, 1)).Value.Number);
    }

    private sealed class OpaqueSource : IFormulaDataSource
    {
        public FormulaCellData GetCell(FormulaCellAddress address)
        {
            Assert.Equal("opaque-id", address.SheetId);
            return FormulaCellData.FromValue(FormulaValue.FromNumber(42));
        }

        public bool TryResolveSheet(string name, out string sheetId)
        {
            sheetId = "opaque-id";
            return name == "Visible";
        }

        public bool TryResolveName(string? qualifiedSheet, string name, string currentSheetId, out FormulaRange range)
        {
            range = default;
            return false;
        }
    }

    private sealed class ThrowingSource(string failingMethod) : IFormulaDataSource
    {
        public FormulaCellData GetCell(FormulaCellAddress address)
        {
            ThrowIfRequested(nameof(GetCell));
            return FormulaCellData.Blank;
        }

        public bool TryResolveSheet(string name, out string sheetId)
        {
            ThrowIfRequested(nameof(TryResolveSheet));
            sheetId = name;
            return true;
        }

        public bool TryResolveName(string? qualifiedSheet, string name, string currentSheetId, out FormulaRange range)
        {
            ThrowIfRequested(nameof(TryResolveName));
            range = default;
            return false;
        }

        private void ThrowIfRequested(string method)
        {
            if (method == failingMethod) throw new InvalidOperationException(method + " failed.");
        }
    }
}
