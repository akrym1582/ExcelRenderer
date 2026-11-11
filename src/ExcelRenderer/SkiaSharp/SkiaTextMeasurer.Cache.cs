using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Accounts immutable cache payloads without retaining native font resources.</summary>
public sealed partial class SkiaTextMeasurer
{
    private int generation = -1;

    private static long LayoutUnits(TextLayoutResult result) => result.Lines.Sum(LineUnits);

    private static long LineUnits(TextLayoutLine line) => line.Text.Length + 1L + line.Runs.Sum(run =>
        1L + run.Run.Text.Length + run.Run.SourceText.Length + run.Run.Font.Family.Length + (run.Glyphs is { } glyphs ?
            glyphs.Source.Count + (glyphs.Glyphs.Count * 7L) : 0));

    private void CheckGeneration()
    {
        var current = (_fontManager as FontManager)?.RegistrationGeneration ?? 0;
        if (current == generation)
        {
            return;
        }

        _layouts?.Clear();
        widths?.Clear();
        lineLayouts?.Clear();
        richLayouts.Clear();
        standaloneRichLayouts.Clear();
        generation = current;
    }

    private readonly record struct MetricFontKey(string Family, double Size, bool Bold, bool Italic)
    {
        internal static MetricFontKey From(FontStyle font) => new(font.Family, font.Size, font.Bold, font.Italic);
    }
}
