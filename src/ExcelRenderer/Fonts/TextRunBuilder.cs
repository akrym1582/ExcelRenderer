using System.Text;

namespace ExcelRenderer.Fonts;

/// <summary>Coalesces contiguous physical-font runs without copying their growing prefixes.</summary>
internal sealed class TextRunBuilder
{
    private readonly List<TextRun> runs = new();
    private TextRun? pending;
    private StringBuilder? display;
    private StringBuilder? source;

    /// <summary>Appends element metadata, allocating a run only when a new physical segment starts.</summary>
    /// <param name="text">Selected display text.</param>
    /// <param name="sourceText">Original Unicode element.</param>
    /// <param name="start">Original UTF-16 offset.</param>
    /// <param name="font">Selected immutable physical face.</param>
    /// <param name="missingIvs">Whether an IVS is missing.</param>
    /// <param name="missingPrivateUse">Whether a private-use glyph is missing.</param>
    /// <param name="glyphId">An explicitly selected variation glyph.</param>
    /// <param name="colorEmojiGlyphId">An explicitly selected color glyph.</param>
    /// <param name="defaultVariation">Whether this is a default variation glyph.</param>
    internal void Add(string text, string sourceText, int start, ResolvedFont font, bool missingIvs, bool missingPrivateUse, ushort? glyphId, ushort? colorEmojiGlyphId, bool defaultVariation)
    {
        if (pending is not null && pending.Font.FaceId == font.FaceId &&
            glyphId is null && colorEmojiGlyphId is null && pending.GlyphId is null &&
            pending.ColorEmojiGlyphId is null && pending.MissingIvsGlyph == missingIvs &&
            pending.MissingPrivateUseGlyph == missingPrivateUse &&
            pending.Utf16Start + (source?.Length ?? pending.SourceText.Length) == start)
        {
            display ??= new(pending.Text);
            source ??= new(pending.SourceText);
            display.Append(text);
            source.Append(sourceText);
            return;
        }

        Flush();
        pending = new(text, font)
        {
            Utf16Start = start,
            SourceText = sourceText,
            MissingIvsGlyph = missingIvs,
            MissingPrivateUseGlyph = missingPrivateUse,
            GlyphId = glyphId,
            ColorEmojiGlyphId = colorEmojiGlyphId,
            IsDefaultVariationGlyph = defaultVariation,
        };
    }

    /// <summary>Completes the immutable run list.</summary>
    /// <returns>Contiguous runs in original source order.</returns>
    internal IReadOnlyList<TextRun> Complete()
    {
        Flush();
        return runs.AsReadOnly();
    }

    private void Flush()
    {
        if (pending is null)
        {
            return;
        }

        runs.Add(display is null ? pending : pending with { Text = display.ToString(), SourceText = source!.ToString() });
        pending = null;
        display = null;
        source = null;
    }
}
