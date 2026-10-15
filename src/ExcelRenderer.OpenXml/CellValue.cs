using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable CellValue snapshot. XML attribute names and source units are preserved.</summary>
public sealed class CellValue
{
    /// <summary>Initializes a new instance of the <see cref="CellValue"/> class.</summary>
    /// <param name="type">The stored value category.</param>
    /// <param name="text">The text value.</param>
    /// <param name="number">The number value.</param>
    /// <param name="boolean">The boolean value.</param>
    internal CellValue(CellValueType type, string? text, double? number, bool? boolean)
    {
        Type = type;
        Text = text;
        Number = number;
        Boolean = boolean;
    }

    /// <summary>Gets the value kind: Blank, String, Number, Boolean, Error or IsoDate.</summary>
    public string Kind => Type switch
    {
        CellValueType.Blank => "Blank",
        CellValueType.String => "String",
        CellValueType.Number => "Number",
        CellValueType.Boolean => "Boolean",
        CellValueType.Error => "Error",
        CellValueType.IsoDate => "IsoDate",
        _ => throw new System.InvalidOperationException("Unknown stored value category."),
    };

    /// <summary>Gets the typed value discriminator for adapter dispatch and conversion cache keys.</summary>
    public CellValueType Type { get; }

    /// <summary>Gets the decoded string, raw Excel error or unchanged ISO date text, depending on Kind.</summary>
    public string? Text { get; }

    /// <summary>Gets the unmodified finite double for a numeric value.</summary>
    public double? Number { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? Boolean { get; }
}
