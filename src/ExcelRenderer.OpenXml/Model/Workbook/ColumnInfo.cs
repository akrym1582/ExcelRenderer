using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable ColumnInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class ColumnInfo
{
    /// <summary>Initializes a new instance of the <see cref="ColumnInfo"/> class.</summary>
    /// <param name="min">The min value.</param>
    /// <param name="max">The max value.</param>
    /// <param name="width">The width value.</param>
    /// <param name="customWidth">The customWidth value.</param>
    /// <param name="hidden">The hidden value.</param>
    /// <param name="styleIndex">The styleIndex value.</param>
    /// <param name="outlineLevel">The outlineLevel value.</param>
    /// <param name="collapsed">The collapsed value.</param>
    internal ColumnInfo(int min, int max, double? width, bool? customWidth, bool? hidden, int? styleIndex, int? outlineLevel, bool? collapsed)
    {
        Min = min;
        Max = max;
        Width = width;
        CustomWidth = customWidth;
        Hidden = hidden;
        StyleIndex = styleIndex;
        OutlineLevel = outlineLevel;
        Collapsed = collapsed;
    }

    /// <summary>Gets the Min metadata.</summary>
    public int Min { get; }

    /// <summary>Gets the Max metadata.</summary>
    public int Max { get; }

    /// <summary>Gets the saved Excel column width, without font measurement or ClosedXML width correction.</summary>
    public double? Width { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? CustomWidth { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? Hidden { get; }

    /// <summary>Gets the StyleIndex metadata.</summary>
    public int? StyleIndex { get; }

    /// <summary>Gets the OutlineLevel metadata.</summary>
    public int? OutlineLevel { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool? Collapsed { get; }
}
