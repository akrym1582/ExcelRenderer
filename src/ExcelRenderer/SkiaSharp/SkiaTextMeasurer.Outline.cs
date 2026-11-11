using ExcelRenderer.Abstractions;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Adapts ordinary single-face paragraphs to Core's shared outline breaker.</summary>
public sealed partial class SkiaTextMeasurer
{
    private static ushort[] SelectGlyphs(TextRun run, SkiaFontFace face)
    {
        Rendering.ConversionMetrics.Report("textGlyphCalls", 1);
        Rendering.ConversionMetrics.Report("textGlyphCharacters", run.Text.Length);
        return (run.GlyphId ?? run.ColorEmojiGlyphId) is { } glyph ? [glyph] : face.Typeface.GetGlyphs(run.Text);
    }

    private IEnumerable<TextLayoutLine> CreateLines(string text, FontStyle font, FontRequest request, double width, bool wrap)
    {
        // Finalize unwrapped lines directly; custom resolvers retain their complete-line input.
        if (!wrap || width <= 0 || _fontManager is not FontManager || !OutlineParagraph.IsValidUtf16(text))
        {
            foreach (var line in TextLineBreaker.Break(text, width, wrap, value => MeasureWidth(value, font, request)))
            {
                ConversionFontResources.Current!.Token.ThrowIfCancellationRequested();
                yield return CreateLine(line.Text, line.ExplicitBreak, font, request);
            }

            yield break;
        }

        var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < paragraphs.Length; index++)
        {
            var paragraph = paragraphs[index];
            var runs = _fontManager.ResolveTextRuns(paragraph, request);
            var run = runs.Count == 1 ? runs[0] : null;
            var face = run is null ? null : ConversionFontResources.Current!.GetFace(run.Font);
            if (run is null || face!.Color || run.GlyphId is not null || run.ColorEmojiGlyphId is not null ||
                run.MissingIvsGlyph || run.MissingPrivateUseGlyph || run.Text != paragraph || run.SourceText != paragraph)
            {
                Rendering.ConversionMetrics.Report("outlineCompatibilityParagraphs", 1);
                var lines = TextLineBreaker.Break(paragraph, width, wrap, value => MeasureWidth(value, font, request)).GetEnumerator();
                using (lines)
                {
                    lines.MoveNext();
                    var previous = lines.Current;
                    while (lines.MoveNext())
                    {
                        yield return CreateLine(previous.Text, false, font, request);
                        previous = lines.Current;
                    }

                    yield return CreateLine(previous.Text, index < paragraphs.Length - 1, font, request);
                }

                continue;
            }

            Rendering.ConversionMetrics.Report("outlineParagraphs", 1);
            using var selected = new OutlineParagraph(paragraph, face, ConversionFontResources.Current!.Token).Break(font.Size, width, wrap).GetEnumerator();
            selected.MoveNext();
            var pending = selected.Current;
            while (selected.MoveNext())
            {
                yield return CreateSelected(pending, false);
                pending = selected.Current;
            }

            yield return CreateSelected(pending, index < paragraphs.Length - 1);

            TextLayoutLine CreateSelected((string Text, ushort[] Glyphs) line, bool explicitBreak) => CreateLine(
                line.Text,
                explicitBreak,
                font,
                request,
                run with { Text = line.Text, SourceText = line.Text, Utf16Start = 0 },
                line.Glyphs);
        }
    }
}
