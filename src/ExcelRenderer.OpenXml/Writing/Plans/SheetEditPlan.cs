using System.Collections.Generic;

namespace ExcelRenderer.OpenXml.Writing.Plans;

/// <summary>A typed, evaluated editing plan; contains no template language.</summary>
public sealed class SheetEditPlan
{
    /// <summary>Initializes a new instance of the <see cref="SheetEditPlan"/> class.</summary>
    /// <param name="sheetIndex">Original one-based worksheet index.</param>
    /// <param name="sourceLastRow">Last template row represented by the plan.</param>
    /// <param name="rows">Final row occurrences in output order.</param>
    /// <param name="rowBreaks">Additional manual row breaks after output rows.</param>
    /// <param name="columnBreaks">Additional manual column breaks after output columns.</param>
    /// <param name="explicitPageBreak">Whether a surviving directive enables explicit scaling.</param>
    public SheetEditPlan(int sheetIndex, int sourceLastRow, IReadOnlyList<OutputRowOccurrence> rows, IReadOnlyList<int> rowBreaks, IReadOnlyList<int> columnBreaks, bool explicitPageBreak)
    {
        SheetIndex = sheetIndex;
        SourceLastRow = sourceLastRow;
        Rows = rows;
        RowBreaks = rowBreaks;
        ColumnBreaks = columnBreaks;
        ExplicitPageBreak = explicitPageBreak;
    }

    /// <summary>Gets the original one-based worksheet index.</summary>
    public int SheetIndex { get; }

    /// <summary>Gets the last template row represented by the plan.</summary>
    public int SourceLastRow { get; }

    /// <summary>Gets the final row occurrences in output order.</summary>
    public IReadOnlyList<OutputRowOccurrence> Rows { get; }

    /// <summary>Gets the additional manual row breaks after output rows.</summary>
    public IReadOnlyList<int> RowBreaks { get; }

    /// <summary>Gets the additional manual column breaks after output columns.</summary>
    public IReadOnlyList<int> ColumnBreaks { get; }

    /// <summary>Gets a value indicating whether a surviving directive enables explicit scaling.</summary>
    public bool ExplicitPageBreak { get; }
}
