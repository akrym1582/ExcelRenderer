using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable CellRecord snapshot. XML attribute names and source units are preserved.</summary>
public sealed class CellRecord
{
    /// <summary>Initializes a new instance of the <see cref="CellRecord"/> class.</summary>
    /// <param name="sheetIndex">The sheetIndex value.</param>
    /// <param name="address">The address value.</param>
    /// <param name="explicitStyleIndex">The explicitStyleIndex value.</param>
    /// <param name="styleIndex">The styleIndex value.</param>
    /// <param name="value">The value value.</param>
    /// <param name="rawValue">The rawValue value.</param>
    /// <param name="formula">The formula value.</param>
    /// <param name="sharedStringIndex">The sharedStringIndex value.</param>
    /// <param name="richTextRuns">The richTextRuns value.</param>
    /// <param name="hasPhonetic">The hasPhonetic value.</param>
    internal CellRecord(int sheetIndex, CellAddress address, int? explicitStyleIndex, int styleIndex, CellValue value, string? rawValue, FormulaInfo? formula, int? sharedStringIndex, IReadOnlyList<RichTextRunInfo> richTextRuns, bool hasPhonetic)
    {
        SheetIndex = sheetIndex;
        Address = address;
        ExplicitStyleIndex = explicitStyleIndex;
        StyleIndex = styleIndex;
        Value = value;
        RawValue = rawValue;
        Formula = formula;
        SharedStringIndex = sharedStringIndex;
        RichTextRuns = richTextRuns;
        HasPhonetic = hasPhonetic;
    }

    /// <summary>Gets the original one-based sheet position, retaining non-worksheet positions.</summary>
    public int SheetIndex { get; }

    /// <summary>Gets the Address metadata.</summary>
    public CellAddress Address { get; }

    /// <summary>Gets the saved cell s attribute, or null when omitted. Zero is an explicit override.</summary>
    public int? ExplicitStyleIndex { get; }

    /// <summary>Gets the effective style index after explicit-cell, custom-row, column and default precedence.</summary>
    public int StyleIndex { get; }

    /// <summary>Gets the Value metadata.</summary>
    public CellValue Value { get; }

    /// <summary>Gets the original v text, distinguishing missing from saved empty text.</summary>
    public string? RawValue { get; }

    /// <summary>Gets the saved formula or array-group metadata. No formula is evaluated.</summary>
    public FormulaInfo? Formula { get; }

    /// <summary>Gets the SharedStringIndex metadata.</summary>
    public int? SharedStringIndex { get; }

    /// <summary>Gets the shared immutable text runs with nullable partial font properties.</summary>
    public IReadOnlyList<RichTextRunInfo> RichTextRuns { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool HasPhonetic { get; }
}
