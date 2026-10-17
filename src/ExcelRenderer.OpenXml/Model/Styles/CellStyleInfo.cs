using System.Collections.Generic;
using ExcelRenderer.OpenXml.Model.Workbook;

namespace ExcelRenderer.OpenXml.Model.Styles;

/// <summary>Immutable CellStyleInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class CellStyleInfo
{
    /// <summary>Initializes a new instance of the <see cref="CellStyleInfo"/> class.</summary>
    /// <param name="styleIndex">The styleIndex value.</param>
    /// <param name="fontId">The fontId value.</param>
    /// <param name="fillId">The fillId value.</param>
    /// <param name="borderId">The borderId value.</param>
    /// <param name="numberFormatId">The numberFormatId value.</param>
    /// <param name="xfId">The xfId value.</param>
    /// <param name="numberFormatCode">The numberFormatCode value.</param>
    /// <param name="font">The font value.</param>
    /// <param name="fill">The fill value.</param>
    /// <param name="border">The border value.</param>
    /// <param name="alignment">The alignment value.</param>
    /// <param name="attributes">The attributes value.</param>
    internal CellStyleInfo(int styleIndex, int fontId, int fillId, int borderId, int numberFormatId, int? xfId, string? numberFormatCode, ElementInfo font, ElementInfo fill, ElementInfo border, ElementInfo? alignment, IReadOnlyDictionary<string, string> attributes)
    {
        StyleIndex = styleIndex;
        FontId = fontId;
        FillId = fillId;
        BorderId = borderId;
        NumberFormatId = numberFormatId;
        XfId = xfId;
        NumberFormatCode = numberFormatCode;
        Font = font;
        FontProperties = FontInfo.FromElement(font);
        Fill = fill;
        Border = border;
        Alignment = alignment;
        Attributes = attributes;
    }

    /// <summary>Gets the StyleIndex metadata.</summary>
    public int StyleIndex { get; }

    /// <summary>Gets the FontId metadata.</summary>
    public int FontId { get; }

    /// <summary>Gets the FillId metadata.</summary>
    public int FillId { get; }

    /// <summary>Gets the BorderId metadata.</summary>
    public int BorderId { get; }

    /// <summary>Gets the NumberFormatId metadata.</summary>
    public int NumberFormatId { get; }

    /// <summary>Gets the XfId metadata.</summary>
    public int? XfId { get; }

    /// <summary>Gets the custom or explicitly supported built-in code; null for unknown regional built-ins.</summary>
    public string? NumberFormatCode { get; }

    /// <summary>Gets the Font metadata.</summary>
    public ElementInfo Font { get; }

    /// <summary>Gets the Fill metadata.</summary>
    public ElementInfo Fill { get; }

    /// <summary>Gets the Border metadata.</summary>
    public ElementInfo Border { get; }

    /// <summary>Gets the saved alignment, or parent alignment when omitted.</summary>
    public ElementInfo? Alignment { get; }

    /// <summary>Gets the original cell-XF attributes, including apply flags and quotePrefix.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>Gets the saved font properties without applying product normalization or fallback.</summary>
    public FontInfo FontProperties { get; }
}
