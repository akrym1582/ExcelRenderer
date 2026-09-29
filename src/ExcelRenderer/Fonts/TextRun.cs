namespace ExcelRenderer.Fonts;

/// <summary>一つのフォントフェイスで描画する、Unicode テキスト要素境界のテキスト範囲を表します。</summary>
public sealed record TextRun(string Text, ResolvedFont Font)
{
    /// <summary>Gets a value indicating the zero-based UTF-16 offset in the original string.</summary>
    public int Utf16Start { get; init; }

    /// <summary>Gets the unmodified source text represented by this run.</summary>
    public string SourceText { get; init; } = Text;

    /// <summary>Gets a value indicating whether an IVS could not be represented by any bundled IVS font.</summary>
    public bool MissingIvsGlyph { get; init; }

    /// <summary>Gets the font-specific glyph selected by an OpenType format 14 mapping, or null for ordinary text.</summary>
    public ushort? GlyphId { get; init; }

    /// <summary>Gets a color emoji glyph ID, rendered as a bitmap in PDF and SVG.</summary>
    public ushort? ColorEmojiGlyphId { get; init; }

    /// <summary>Gets a value indicating whether the variation sequence uses its default UVS mapping.</summary>
    public bool IsDefaultVariationGlyph { get; init; }
}
