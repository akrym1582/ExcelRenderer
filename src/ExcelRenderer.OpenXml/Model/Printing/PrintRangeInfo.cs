using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable PrintRangeInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class PrintRangeInfo
{
    /// <summary>Initializes a new instance of the <see cref="PrintRangeInfo"/> class.</summary>
    /// <param name="kind">The kind value.</param>
    /// <param name="firstRow">The firstRow value.</param>
    /// <param name="lastRow">The lastRow value.</param>
    /// <param name="firstColumn">The firstColumn value.</param>
    /// <param name="lastColumn">The lastColumn value.</param>
    internal PrintRangeInfo(string kind, int firstRow, int lastRow, int firstColumn, int lastColumn)
    {
        Kind = kind;
        FirstRow = firstRow;
        LastRow = lastRow;
        FirstColumn = firstColumn;
        LastColumn = lastColumn;
    }

    /// <summary>Gets the Kind metadata.</summary>
    public string Kind { get; }

    /// <summary>Gets the FirstRow metadata.</summary>
    public int FirstRow { get; }

    /// <summary>Gets the LastRow metadata.</summary>
    public int LastRow { get; }

    /// <summary>Gets the FirstColumn metadata.</summary>
    public int FirstColumn { get; }

    /// <summary>Gets the LastColumn metadata.</summary>
    public int LastColumn { get; }
}
