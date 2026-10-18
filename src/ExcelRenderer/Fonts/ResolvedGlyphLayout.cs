using ExcelRenderer.Abstractions;

namespace ExcelRenderer.Fonts;

/// <summary>Freezes physical glyph selection before any output renderer consumes a run.</summary>
internal static class ResolvedGlyphLayout
{
    /// <summary>Completes a run without changing its text, advance, style or position.</summary>
    /// <param name="run">The caller's resolved run.</param>
    /// <returns>A handle-free snapshot-backed run.</returns>
    internal static TextLayoutRun Finalize(TextLayoutRun run)
    {
        if (run.Glyphs is not null)
        {
            return run;
        }

        var resources = ConversionFontResources.Current ?? throw new InvalidOperationException("A font resource scope is required to finalize glyphs.");
        var text = run.Run;
        var face = resources.GetFace(text.Font);
        var glyphs = (text.GlyphId ?? text.ColorEmojiGlyphId) is { } glyph ? new[] { glyph } : face.Typeface.GetGlyphs(text.Text);
        return run with
        {
            Run = text with { Font = resources.Freeze(text.Font) },
            Glyphs = new(face, glyphs, text.Text, text.SourceText, text.GlyphId is not null || text.ColorEmojiGlyphId is not null || text.MissingPrivateUseGlyph),
        };
    }
}
