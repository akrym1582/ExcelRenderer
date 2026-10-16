using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class StaticDefinitionTests
{
    [Theory]
    [InlineData("'Sheet 1'!$A$1:$B$2")]
    [InlineData("Sheet1!$A:$C,'Sheet 1'!$1:$3")]
    [InlineData("'O''Brien, west'!$XFD$1048576")]
    [InlineData("123")]
    [InlineData("=-1.25E+3")]
    [InlineData("\"literal,+SUM(A1),\"\"quotes\"\"\"")]
    [InlineData("TRUE")]
    [InlineData("$A$1")]
    public void StaticReferencesAndLiteralConstantsRequireNoCalculation(string text) =>
        Assert.True(WorkbookFormulaInspection.IsStaticDefinition(text, ["Sheet1", "Sheet 1", "O'Brien, west"]));

    [Theory]
    [InlineData("SUM(A1:A2)")]
    [InlineData("A1+1")]
    [InlineData("OFFSET(Sheet1!A1,0,0)")]
    [InlineData("INDIRECT(\"A1\")")]
    [InlineData("OtherName")]
    [InlineData("[external.xlsx]Sheet1!A1")]
    [InlineData("Sheet1!A1#")]
    [InlineData("Sheet1!A0")]
    [InlineData("Sheet1!XFE1")]
    [InlineData("A1048577")]
    [InlineData("#REF!")]
    [InlineData("Sheet1!A1,")]
    [InlineData("")]
    public void DynamicOrUnknownDefinitionsAreNeverTreatedAsFormulaFree(string text) =>
        Assert.False(WorkbookFormulaInspection.IsStaticDefinition(text, ["Sheet1"]));
}
