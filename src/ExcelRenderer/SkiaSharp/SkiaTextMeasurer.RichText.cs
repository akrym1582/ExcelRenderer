using System.Globalization;
using System.Text;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Finalizes partial formatting using whole-string Unicode element boundaries.</summary>
public sealed partial class SkiaTextMeasurer
{
    private readonly Dictionary<RichTextLayoutKey, TextLayoutResult> richLayouts = new();
    private readonly Dictionary<RichTextLayoutKey, WeakReference<TextLayoutResult>> standaloneRichLayouts = new();

    /// <inheritdoc/>
    public TextLayoutResult Layout(IReadOnlyList<CellTextRun> runs, FontStyle font, double availableWidth, bool wrap)
    {
        if (borrowConversionScope)
        {
            return LayoutCore(runs, font, availableWidth, wrap);
        }

        lock (standaloneSync)
        {
            return LayoutCore(runs, font, availableWidth, wrap);
        }
    }

    private static IEnumerable<(string Text, FontStyle Font)> Segments(IReadOnlyList<CellTextRun> elements)
    {
        var index = 0;
        while (index < elements.Count)
        {
            var style = elements[index].Font;
            var text = new StringBuilder();
            do
            {
                text.Append(elements[index++].Text);
            }
            while (index < elements.Count && elements[index].Font == style);

            yield return (text.ToString(), style);
        }
    }

    private TextLayoutResult LayoutCore(IReadOnlyList<CellTextRun> runs, FontStyle font, double availableWidth, bool wrap)
    {
        if (runs is null)
        {
            throw new ArgumentNullException(nameof(runs));
        }

        var snapshot = runs.ToArray();
        if (snapshot.Any(run => run is null || run.Text is null || run.Font is null || !double.IsFinite(run.Font.Size) || run.Font.Size <= 0))
        {
            throw new ArgumentException("Partial text formatting must have non-null text and positive finite font sizes.", nameof(runs));
        }

        var text = string.Concat(snapshot.Select(run => run.Text));
        var key = new RichTextLayoutKey(snapshot, font, availableWidth, wrap, (_fontManager as FontManager)?.RegistrationGeneration ?? 0);
        if (borrowConversionScope && richLayouts.TryGetValue(key, out var cached))
        {
            Rendering.ConversionMetrics.Report("richTextLayoutCacheHits", 1);
            return cached;
        }

        if (!borrowConversionScope && _fontManager is FontManager && standaloneRichLayouts.TryGetValue(key, out var reference) && reference.TryGetTarget(out var standaloneCached))
        {
            Rendering.ConversionMetrics.Report("richTextLayoutCacheHits", 1);
            return standaloneCached;
        }

        Rendering.ConversionMetrics.Report("richTextLayoutCacheMisses", 1);
        using var resources = !BorrowsResources || ConversionFontResources.Current is null ? new ConversionFontResources() : null;
        var elements = StringInfo.GetTextElementEnumerator(text);
        var current = new List<CellTextRun>();
        var lines = new List<TextLayoutLine>();
        var sourceIndex = 0;
        var sourceEnd = snapshot.Length == 0 ? 0 : snapshot[0].Text.Length;
        while (elements.MoveNext())
        {
            while (sourceIndex + 1 < snapshot.Length && elements.ElementIndex >= sourceEnd)
            {
                sourceEnd += snapshot[++sourceIndex].Text.Length;
            }

            var element = elements.GetTextElement();
            if (element is "\n" or "\r\n")
            {
                lines.Add(CreateRichLine(current, font, true));
                current.Clear();
                continue;
            }

            var item = new CellTextRun(element, snapshot[sourceIndex].Font);
            current.Add(item);
            if (wrap && availableWidth > 0 && current.Count > 1 && !Core.Layout.TextLineBreaker.Fits(MeasureRichWidth(current), availableWidth))
            {
                current.RemoveAt(current.Count - 1);
                lines.Add(CreateRichLine(current, font, false));
                current.Clear();
                current.Add(item);
            }
        }

        if (text.Length > 0)
        {
            lines.Add(CreateRichLine(current, font, false));
        }

        var result = new TextLayoutResult(new(lines.Count == 0 ? 0 : lines.Max(line => line.Width), lines.Sum(line => line.Height)), lines.AsReadOnly())
        {
            EffectiveFontSize = font.Size,
        };
        if (borrowConversionScope && text.Length <= 2048)
        {
            if (richLayouts.Count >= 512)
            {
                richLayouts.Clear();
            }

            richLayouts[key] = result;
        }
        else if (_fontManager is FontManager && text.Length <= 2048)
        {
            if (standaloneRichLayouts.Count >= 8)
            {
                standaloneRichLayouts.Clear();
            }

            standaloneRichLayouts[key] = new(result);
        }

        return result;
    }

    private double MeasureRichWidth(IReadOnlyList<CellTextRun> elements)
    {
        var width = 0d;
        foreach (var (text, style) in Segments(elements))
        {
            width += MeasureWidth(text, style, new(style.Family, style.Bold ? 700 : 400, style.Italic));
        }

        return width;
    }

    private TextLayoutLine CreateRichLine(IReadOnlyList<CellTextRun> elements, FontStyle emptyFont, bool explicitBreak)
    {
        if (elements.Count == 0)
        {
            return CreateLine(string.Empty, explicitBreak, emptyFont, new(emptyFont.Family, emptyFont.Bold ? 700 : 400, emptyFont.Italic));
        }

        var runs = new List<TextLayoutRun>();
        var x = 0d;
        var ascent = 0d;
        var descent = 0d;
        var leading = 0d;
        foreach (var (text, style) in Segments(elements))
        {
            var line = CreateLine(text, false, style, new(style.Family, style.Bold ? 700 : 400, style.Italic));
            runs.AddRange(line.Runs.Select(run => run with { X = run.X + x, Font = style }));
            x += line.Width;
            ascent = Math.Max(ascent, line.Ascent);
            descent = Math.Max(descent, line.Descent);
            leading = Math.Max(leading, line.Leading);
        }

        return new(string.Concat(elements.Select(element => element.Text)), x, ascent + descent + leading, ascent, runs.AsReadOnly(), explicitBreak)
        {
            Ascent = ascent,
            Descent = descent,
            Leading = leading,
        };
    }
}
