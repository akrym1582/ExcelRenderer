using System.Globalization;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Layout;

/// <summary>Owns one ordinary outline paragraph's glyphs and breaks only at Unicode element boundaries.</summary>
internal sealed class OutlineParagraph
{
    private readonly string text;
    private readonly SkiaFontFace face;
    private readonly ushort[] glyphs;
    private readonly CancellationToken token;

    /// <summary>Initializes a new instance of the <see cref="OutlineParagraph"/> class.</summary>
    /// <param name="text">A valid UTF-16 paragraph with unchanged display/source text.</param>
    /// <param name="face">Its single noncolor physical face.</param>
    /// <param name="token">The owning conversion's cancellation token.</param>
    internal OutlineParagraph(string text, SkiaFontFace face, CancellationToken token)
    {
        this.text = text;
        this.face = face;
        this.token = token;
        token.ThrowIfCancellationRequested();
        glyphs = face.Typeface.GetGlyphs(text);
        ConversionMetrics.Report("textGlyphCalls", 1);
        ConversionMetrics.Report("textGlyphCharacters", text.Length);
    }

    /// <summary>Checks whether scalar-to-glyph indexing is well-defined.</summary>
    /// <param name="text">The paragraph.</param>
    /// <returns>Whether no unmatched surrogate is present.</returns>
    internal static bool IsValidUtf16(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index == text.Length || !char.IsLowSurrogate(text[index]))
                {
                    return false;
                }
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Enumerates lines while preserving the existing integer-run advance conversion.</summary>
    /// <param name="size">The run size.</param>
    /// <param name="width">The available width.</param>
    /// <param name="wrap">Whether automatic wrapping is enabled.</param>
    /// <returns>Line text and already selected glyphs; source clusters are rebuilt per line.</returns>
    internal IEnumerable<(string Text, ushort[] Glyphs)> Break(double size, double width, bool wrap)
    {
        if (!wrap || width <= 0)
        {
            yield return (text, glyphs);
            yield break;
        }

        foreach (var line in BreakRanges(size, width, wrap))
        {
            ConversionMetrics.Report("outlineIntermediateGlyphCopies", 1);
            yield return (line.Text, glyphs.AsSpan(line.GlyphStart, line.GlyphCount).ToArray());
        }
    }

    /// <summary>Copies a borrowed paragraph range only into the final immutable selection.</summary>
    /// <param name="line">The complete line text.</param>
    /// <param name="start">The paragraph glyph offset.</param>
    /// <param name="count">The selected glyph count.</param>
    /// <returns>A selection that does not retain the paragraph buffer.</returns>
    internal GlyphSequence CreateSelection(string line, int start, int count) =>
        new(face, glyphs.AsSpan(start, count), line, line);

    /// <summary>Enumerates glyph ranges without allocating temporary line glyph arrays.</summary>
    /// <param name="size">The run size.</param>
    /// <param name="width">The available width.</param>
    /// <param name="wrap">Whether automatic wrapping is enabled.</param>
    /// <returns>Line text and paragraph-relative glyph ranges.</returns>
    internal IEnumerable<(string Text, int GlyphStart, int GlyphCount)> BreakRanges(double size, double width, bool wrap)
    {
        if (!wrap || width <= 0)
        {
            yield return (text, 0, glyphs.Length);
            yield break;
        }

        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var start = 0;
        var firstGlyph = 0;
        var glyphIndex = 0;
        long units = 0;
        for (var index = 0; index < boundaries.Length; index++)
        {
            if ((index & 255) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            var elementStart = boundaries[index];
            var elementEnd = index + 1 < boundaries.Length ? boundaries[index + 1] : text.Length;
            var elementGlyph = glyphIndex;
            long elementUnits = 0;
            for (var character = elementStart; character < elementEnd; character++)
            {
                if ((character & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                elementUnits += face.AdvanceUnits(glyphs[glyphIndex++]);
                if (char.IsHighSurrogate(text[character]))
                {
                    character++;
                }
            }

            ConversionMetrics.Report("textWrapComparisons", 1);
            if (elementStart > start && !TextLineBreaker.Fits(face.MeasureUnits(units + elementUnits, size), width))
            {
                yield return (text.Substring(start, elementStart - start), firstGlyph, elementGlyph - firstGlyph);
                start = elementStart;
                firstGlyph = elementGlyph;
                units = 0;
            }

            units += elementUnits;
        }

        yield return (text.Substring(start), firstGlyph, glyphs.Length - firstGlyph);
    }
}
