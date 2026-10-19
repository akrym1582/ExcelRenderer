using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class SavedFormulaTests
{
    [Theory]
    [InlineData("999", "Number")]
    [InlineData("saved", "String")]
    [InlineData("1", "Boolean")]
    [InlineData("#DIV/0!", "Error")]
    [InlineData(null, "Number")]
    public async Task CoreUsesSavedResultsWithoutEvaluatingUnsupportedFormula(string? value, string type)
    {
        var (result, pdf) = await TestSupport.Convert(workbook =>
        {
            var cell = TestSupport.Sheet(workbook).Descendants<S.Cell>().Single();
            cell.InlineString = null;
            cell.DataType = type switch
            {
                "String" => S.CellValues.String,
                "Boolean" => S.CellValues.Boolean,
                "Error" => S.CellValues.Error,
                _ => S.CellValues.Number,
            };
            cell.CellFormula = new("UNKNOWN()");
            cell.CellValue = value is null ? null : new(value);
        });
        Assert.NotEmpty(pdf);
        using var document = TestSupport.Open(pdf);
        Assert.Single(document.Pages);
        if (value is null)
        {
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FormulaCachedValueMissing");
        }
    }
}
