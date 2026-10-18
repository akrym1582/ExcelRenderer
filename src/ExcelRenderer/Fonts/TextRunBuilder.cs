using System.Text;

namespace ExcelRenderer.Fonts;

/// <summary>Coalesces contiguous physical-font runs without copying their growing prefixes.</summary>
internal sealed class TextRunBuilder
{
    private readonly List<TextRun> runs = new();
    private TextRun? pending;
    private StringBuilder? display;
    private StringBuilder? source;

    /// <summary>Appends a selected source element under the existing merge policy.</summary>
    /// <param name="run">The already resolved element.</param>
    internal void Add(TextRun run)
    {
        if (pending is not null && pending.Font.FaceId == run.Font.FaceId &&
            run.GlyphId is null && run.ColorEmojiGlyphId is null && pending.GlyphId is null &&
            pending.ColorEmojiGlyphId is null && pending.MissingIvsGlyph == run.MissingIvsGlyph &&
            pending.MissingPrivateUseGlyph == run.MissingPrivateUseGlyph &&
            pending.Utf16Start + (source?.Length ?? pending.SourceText.Length) == run.Utf16Start)
        {
            display ??= new(pending.Text);
            source ??= new(pending.SourceText);
            display.Append(run.Text);
            source.Append(run.SourceText);
            return;
        }

        Flush();
        pending = run;
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
