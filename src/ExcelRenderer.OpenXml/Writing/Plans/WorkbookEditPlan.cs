using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>A typed, evaluated editing plan; contains no template language.</summary>
public sealed class WorkbookEditPlan
{
    /// <summary>Initializes a new instance of the <see cref="WorkbookEditPlan"/> class.</summary>
    /// <param name="sheets">Final sheet plans in original workbook order.</param>
    public WorkbookEditPlan(IReadOnlyList<SheetEditPlan> sheets)
        : this(sheets, false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WorkbookEditPlan"/> class with formula rewriting.</summary>
    /// <param name="sheets">Final sheet plans in original workbook order.</param>
    /// <param name="rewriteFormulas">Whether to rewrite supported formulas and invalidate their cached values.</param>
    public WorkbookEditPlan(IReadOnlyList<SheetEditPlan> sheets, bool rewriteFormulas)
    {
        Sheets = sheets;
        RewriteFormulas = rewriteFormulas;
    }

    /// <summary>Gets a value indicating whether formula references are rewritten and caches invalidated.</summary>
    public bool RewriteFormulas { get; }

    /// <summary>Gets the final sheet plans in original workbook order.</summary>
    public IReadOnlyList<SheetEditPlan> Sheets { get; }
}
