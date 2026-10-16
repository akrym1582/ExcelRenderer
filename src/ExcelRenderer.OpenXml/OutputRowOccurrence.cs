using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>A typed, evaluated editing plan; contains no template language.</summary>
public sealed class OutputRowOccurrence
{
    /// <summary>Initializes a new instance of the <see cref="OutputRowOccurrence"/> class.</summary>
    /// <param name="sourceRow">Original template row.</param>
    /// <param name="outputRow">Final one-based output row.</param>
    /// <param name="occurrenceId">Repeat context identity, zero for root rows.</param>
    /// <param name="parentOccurrenceId">Enclosing repeat context identity.</param>
    /// <param name="values">Evaluated cell replacements for this row.</param>
    public OutputRowOccurrence(int sourceRow, int outputRow, int occurrenceId, int parentOccurrenceId, IReadOnlyList<CellValueEdit> values)
    {
        SourceRow = sourceRow;
        OutputRow = outputRow;
        OccurrenceId = occurrenceId;
        ParentOccurrenceId = parentOccurrenceId;
        Values = values;
    }

    /// <summary>Gets the original template row.</summary>
    public int SourceRow { get; }

    /// <summary>Gets the final one-based output row.</summary>
    public int OutputRow { get; }

    /// <summary>Gets the repeat context identity, zero for root rows.</summary>
    public int OccurrenceId { get; }

    /// <summary>Gets the enclosing repeat context identity.</summary>
    public int ParentOccurrenceId { get; }

    /// <summary>Gets the evaluated cell replacements for this row.</summary>
    public IReadOnlyList<CellValueEdit> Values { get; }
}
