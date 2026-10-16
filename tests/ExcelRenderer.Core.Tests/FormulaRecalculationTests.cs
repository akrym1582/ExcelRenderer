using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class FormulaRecalculationTests
{
    [Fact]
    public async Task CorePdfRecalculatesSupportedFormula()
    {
        var (_, pdf) = await TestSupport.Convert(workbook =>
        {
            var cell = TestSupport.Sheet(workbook).Descendants<S.Cell>().Single();
            cell.InlineString = null;
            cell.DataType = S.CellValues.Number;
            cell.CellFormula = new("ROUND(2.5,0)*2");
            cell.CellValue = new("999");
        }, TestSupport.Options with { ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate } });
        Assert.NotEmpty(pdf);
        using var document = TestSupport.Open(pdf);
        Assert.Single(document.Pages);
    }

    [Fact]
    public async Task CoreCalculationFailureDoesNotTouchOutput()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var cell = TestSupport.Sheet(workbook).Descendants<S.Cell>().Single();
            cell.InlineString = null;
            cell.CellFormula = new("UNKNOWN()");
            cell.CellValue = new("999");
        });
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<OpenXml.FormulaCalculationException>(() => ExcelConverter.ConvertAsync(input, output,
            TestSupport.Options with { ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate } }));
        Assert.Empty(output.ToArray());
    }
}
