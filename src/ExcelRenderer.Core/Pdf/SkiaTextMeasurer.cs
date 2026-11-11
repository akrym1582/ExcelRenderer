using System.Globalization;
using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Skia の物理フォントメトリクスを使用して、レイアウトに必要な文字列の寸法を測定します。</summary>
internal sealed class SkiaTextMeasurer : ITextMeasurer, ITextLayoutService
{
    private const int MaximumEntries = 512;
    private const int MaximumRetainedUnits = 131072;
    private readonly SingleFontContext context;
    private readonly CancellationToken token;
    private readonly Dictionary<LayoutKey, CacheEntry> layouts = new();
    private readonly CacheEntry?[] slots = new CacheEntry?[MaximumEntries];
    private int hand;
    private int retainedUnits;

    /// <summary>Initializes a new instance of the <see cref="SkiaTextMeasurer"/> class.</summary>
    /// <param name="context">The conversion font snapshot.</param>
    /// <param name="token">The conversion cancellation token.</param>
    internal SkiaTextMeasurer(SingleFontContext context, CancellationToken token = default)
    {
        this.context = context;
        this.token = token;
    }

    /// <summary>指定したフォントで文字列を測定し、必要に応じて利用可能幅に収まる行数へ折り返した寸法を算出します。</summary>
    /// <param name="text">寸法を測定する文字列です。</param>
    /// <param name="font">測定に使用するフォントファミリー、サイズ、および装飾です。</param>
    /// <param name="availableWidth">折り返し後の一行に利用できる幅をポイント単位で指定します。</param>
    /// <param name="wrap">利用可能幅を超える文字列の高さを複数行分として算出する場合は <see langword="true"/> です。</param>
    /// <returns>折り返さない場合は文字列本来の幅と高さ、折り返す場合は利用可能幅と推定した全行の高さを返します。空文字列の場合は幅と高さがともに 0 です。</returns>
    public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap)
    {
        return Layout(text, font, availableWidth, wrap).Size;
    }

    /// <inheritdoc/>
    public TextLayoutResult Layout(string text, FontStyle font, double availableWidth, bool wrap)
    {
        text = DisplayText.WithoutVariationSelectors(text);
        if (string.IsNullOrEmpty(text))
        {
            return new(new(0, 0), []) { EffectiveFontSize = font.Size };
        }

        var wraps = wrap && !(availableWidth <= 0);
        var key = new LayoutKey(text, font.Size, wraps ? availableWidth : 0, wraps);
        if (layouts.TryGetValue(key, out var cached))
        {
            cached.Referenced = true;
            Rendering.ConversionMetrics.Report("layoutCacheHit", 1);
            return cached.Result;
        }

        Rendering.ConversionMetrics.Report("layoutCacheMiss", 1);

        token.ThrowIfCancellationRequested();
        var lines = (!context.Face.Color && IsValidUtf16(text)
            ? LayoutOutline(text, font, availableWidth, wrap)
            : TextLineBreaker.Break(text, availableWidth, wrap, value => context.Face.Measure(SelectGlyphs(value), font.Size))
                .Select(line => CreateLine(line.Text, line.ExplicitBreak, font))).ToArray();
        var result = new TextLayoutResult(
            new(lines.Length == 0 ? 0 : lines.Max(line => line.Width), lines.Sum(line => line.Height)),
            Array.AsReadOnly(lines))
        {
            EffectiveFontSize = font.Size,
        };
        if (text.Length <= 2048)
        {
            var units = text.Length + lines.Sum(line => line.Text.Length + (line.Glyphs?.Glyphs.Count ?? 0));
            Store(key, result, units);
        }

        return result;
    }

    private static bool IsValidUtf16(string text)
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

    private IEnumerable<TextLayoutLine> LayoutOutline(string text, FontStyle font, double width, bool wrap)
    {
        var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
        {
            token.ThrowIfCancellationRequested();
            var paragraph = paragraphs[paragraphIndex];
            var glyphs = SelectGlyphs(paragraph);
            var explicitBreak = paragraphIndex < paragraphs.Length - 1;
            if (!wrap || width <= 0)
            {
                yield return CreateLine(paragraph, explicitBreak, font, glyphs);
                continue;
            }

            var boundaries = StringInfo.ParseCombiningCharacters(paragraph);
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
                var elementEnd = index + 1 < boundaries.Length ? boundaries[index + 1] : paragraph.Length;
                var elementGlyph = glyphIndex;
                long elementUnits = 0;
                for (var character = elementStart; character < elementEnd; character++)
                {
                    if ((character & 255) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    elementUnits += context.Face.AdvanceUnits(glyphs[glyphIndex++]);
                    if (char.IsHighSurrogate(paragraph[character]))
                    {
                        character++;
                    }
                }

                Rendering.ConversionMetrics.Report("textWrapComparisons", 1);
                if (elementStart > start && !TextLineBreaker.Fits(context.Face.MeasureUnits(units + elementUnits, font.Size), width))
                {
                    yield return CreateLine(paragraph.Substring(start, elementStart - start), false, font, glyphs.AsSpan(firstGlyph, elementGlyph - firstGlyph).ToArray());
                    start = elementStart;
                    firstGlyph = elementGlyph;
                    units = 0;
                }

                units += elementUnits;
            }

            yield return CreateLine(paragraph.Substring(start), explicitBreak, font, glyphs.AsSpan(firstGlyph).ToArray());
        }
    }

    private void Store(LayoutKey key, TextLayoutResult result, int units)
    {
        while (layouts.Count >= MaximumEntries || retainedUnits + units > MaximumRetainedUnits)
        {
            var entry = slots[hand];
            if (entry is not null)
            {
                if (entry.Referenced)
                {
                    entry.Referenced = false;
                }
                else
                {
                    layouts.Remove(entry.Key);
                    retainedUnits -= entry.Units;
                    slots[hand] = null;
                    Rendering.ConversionMetrics.Report("layoutCacheEviction", 1);
                    continue;
                }
            }

            hand = (hand + 1) % MaximumEntries;
        }

        while (slots[hand] is not null)
        {
            hand = (hand + 1) % MaximumEntries;
        }

        var value = new CacheEntry(key, result, units);
        slots[hand] = value;
        layouts.Add(key, value);
        retainedUnits += units;
        hand = (hand + 1) % MaximumEntries;
        Rendering.ConversionMetrics.Report("layoutCacheEntries", layouts.Count);
        Rendering.ConversionMetrics.Report("layoutCacheRetainedUnits", retainedUnits);
    }

    private ushort[] SelectGlyphs(string text)
    {
        Rendering.ConversionMetrics.Report("textGlyphCalls", 1);
        Rendering.ConversionMetrics.Report("textGlyphCharacters", text.Length);
        return context.Face.Typeface.GetGlyphs(text);
    }

    private TextLayoutLine CreateLine(string text, bool explicitBreak, FontStyle style, ushort[]? selectedGlyphs = null)
    {
        var (ascent, descent, leading) = context.Metrics(style.Size);
        var glyphs = selectedGlyphs ?? SelectGlyphs(text);
        return new(text, context.Face.Measure(glyphs, style.Size), ascent + descent + leading, ascent, explicitBreak)
        {
            Glyphs = new(context.Face, glyphs, text, text),
            Ascent = ascent,
            Descent = descent,
            Leading = leading,
        };
    }

    private readonly record struct LayoutKey(string Text, double Size, double Width, bool Wrap);

    private sealed class CacheEntry(LayoutKey key, TextLayoutResult result, int units)
    {
        internal LayoutKey Key { get; } = key;

        internal TextLayoutResult Result { get; } = result;

        internal int Units { get; } = units;

        internal bool Referenced { get; set; } = true;
    }
}
