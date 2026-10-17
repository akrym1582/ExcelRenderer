using System.Collections.Generic;

namespace ExcelRenderer.OpenXml.Model.Workbook;

/// <summary>Immutable SheetInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class SheetInfo
{
    /// <summary>Initializes a new instance of the <see cref="SheetInfo"/> class.</summary>
    /// <param name="index">The index value.</param>
    /// <param name="sheetId">The sheetId value.</param>
    /// <param name="name">The name value.</param>
    /// <param name="kind">The kind value.</param>
    /// <param name="visibility">The visibility value.</param>
    internal SheetInfo(int index, uint sheetId, string name, string kind, string visibility)
    {
        Index = index;
        SheetId = sheetId;
        Name = name;
        Kind = kind;
        Visibility = visibility;
    }

    /// <summary>Gets the Index metadata.</summary>
    public int Index { get; }

    /// <summary>Gets the SheetId metadata.</summary>
    public uint SheetId { get; }

    /// <summary>Gets the Name metadata.</summary>
    public string Name { get; }

    /// <summary>Gets the Kind metadata.</summary>
    public string Kind { get; }

    /// <summary>Gets the Visibility metadata.</summary>
    public string Visibility { get; }
}
