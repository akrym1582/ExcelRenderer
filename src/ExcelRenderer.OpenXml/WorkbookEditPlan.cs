using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>A typed, evaluated editing plan; contains no template language.</summary>
public sealed class WorkbookEditPlan
{
    /// <summary>Initializes a new instance of the <see cref="WorkbookEditPlan"/> class.</summary>
    /// <param name="sheets">Final sheet plans in original workbook order.</param>
    public WorkbookEditPlan(IReadOnlyList<SheetEditPlan> sheets)
    {
        Sheets = sheets;
    }

    /// <summary>Gets the final sheet plans in original workbook order.</summary>
    public IReadOnlyList<SheetEditPlan> Sheets { get; }
}
