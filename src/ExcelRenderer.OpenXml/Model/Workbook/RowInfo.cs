using System.Collections.Generic;

namespace ExcelRenderer.OpenXml.Model.Workbook;

/// <summary>Immutable RowInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class RowInfo
{
    /// <summary>Initializes a new instance of the <see cref="RowInfo"/> class.</summary>
    /// <param name="index">The index value.</param>
    /// <param name="heightPt">The heightPt value.</param>
    /// <param name="customHeight">The customHeight value.</param>
    /// <param name="hidden">The hidden value.</param>
    /// <param name="styleIndex">The styleIndex value.</param>
    /// <param name="customFormat">The customFormat value.</param>
    /// <param name="outlineLevel">The outlineLevel value.</param>
    /// <param name="collapsed">The collapsed value.</param>
    internal RowInfo(int index, double? heightPt, bool? customHeight, bool? hidden, int? styleIndex, bool? customFormat, int? outlineLevel, bool? collapsed)
    {
        Index = index;
        HeightPt = heightPt;
        CustomHeight = customHeight;
        Hidden = hidden;
        StyleIndex = styleIndex;
        CustomFormat = customFormat;
        OutlineLevel = outlineLevel;
        Collapsed = collapsed;
    }

    /// <summary>Gets the Index metadata.</summary>
    public int Index { get; }

    /// <summary>Gets the saved row height in points, or null when omitted.</summary>
    public double? HeightPt { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? CustomHeight { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? Hidden { get; }

    /// <summary>Gets the StyleIndex metadata.</summary>
    public int? StyleIndex { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? CustomFormat { get; }

    /// <summary>Gets the OutlineLevel metadata.</summary>
    public int? OutlineLevel { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? Collapsed { get; }
}
