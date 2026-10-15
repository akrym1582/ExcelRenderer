using ClosedXML.Excel;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Executable limits of the pinned dependency's public recalculation APIs.</summary>
public sealed class FormulaEvaluationScopeTests
{
    [Fact]
    public void ClosedXmlDirtyDependencyEvaluationCanReachUnrelatedSheet()
    {
        using var workbook = new XLWorkbook();
        var broken = workbook.AddWorksheet("unselected");
        broken.Cell(1, 1).FormulaA1 = "A1+1";
        var selected = workbook.AddWorksheet("selected");
        selected.Cell(1, 1).Value = 4;
        selected.Cell(1, 2).FormulaA1 = "A1+1";
        selected.Cell(1, 2).InvalidateFormula();
        Assert.Throws<InvalidOperationException>(() => selected.Evaluate("B1+1", "C1"));
    }
    [Fact]
    public void WorksheetRecalculationUsesStaleCrossSheetDependencyCache()
    {
        using var workbook = new XLWorkbook();
        var dependency = workbook.AddWorksheet("dependency").Cell(1, 1);
        dependency.FormulaA1 = "4+1";
        var selected = workbook.AddWorksheet("selected");
        selected.Cell(1, 1).FormulaA1 = "dependency!A1+1";
        selected.RecalculateAllFormulas();
        Assert.True(dependency.NeedsRecalculation);
        Assert.Equal(1, selected.Cell(1, 1).CachedValue.GetNumber());
        workbook.RecalculateAllFormulas();
        Assert.Equal(6, selected.Cell(1, 1).CachedValue.GetNumber());
    }

    [Fact]
    public void EvaluatingDirectExpressionWorksUntilADirtyFormulaDependencyIsRead()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("unrelated").Cell(1, 1).FormulaA1 = "A1+1";
        var selected = workbook.AddWorksheet("selected");
        selected.Cell(1, 1).Value = 4;
        selected.Cell(1, 2).FormulaA1 = "A1+1";
        Assert.Equal(5, selected.Evaluate(selected.Cell(1, 2).FormulaA1, "B1").GetNumber());
        Assert.True(selected.Cell(1, 2).NeedsRecalculation);
        selected.Cell(1, 3).FormulaA1 = "B1+1";
        Assert.Throws<InvalidOperationException>(() => selected.Evaluate(selected.Cell(1, 3).FormulaA1, "C1"));
    }
}
