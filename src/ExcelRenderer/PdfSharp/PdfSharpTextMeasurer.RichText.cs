using System.Globalization;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace ExcelRenderer.PdfSharp;

/// <summary>Finalizes partial formatting using whole-string Unicode element boundaries.</summary>
public sealed partial class PdfSharpTextMeasurer
{
    private readonly Dictionary<RichTextLayoutKey, TextLayoutResult> richLayouts = new();

    /// <inheritdoc/>
    public TextLayoutResult Layout(IReadOnlyList<CellTextRun> runs, FontStyle font, double availableWidth, bool wrap)
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

        if (_fontManager is null)
        {
            return new PdfSharpTextMeasurer(new FontManager(new FontOptions())).Layout(snapshot, font, availableWidth, wrap);
        }

        var text = string.Concat(snapshot.Select(run => run.Text));
        var key = new RichTextLayoutKey(snapshot, font, availableWidth, wrap, (_fontManager as FontManager)?.RegistrationGeneration ?? 0);
        if (richLayouts.TryGetValue(key, out var cached))
        {
            Rendering.ConversionMetrics.Report("richTextLayoutCacheHits", 1);
            return cached;
        }

        Rendering.ConversionMetrics.Report("richTextLayoutCacheMisses", 1);
        var callerOwnsFontSession = Rendering.RenderResourceSession.Current?.FontSession is not null;
        using var fontSession = callerOwnsFontSession ? null : Core.Rendering.PdfSharpFontGate.Acquire();
        if (!callerOwnsFontSession)
        {
            GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver(_fontManager);
        }

        using var graphics = XGraphics.CreateMeasureContext(new XSize(availableWidth, double.MaxValue), XGraphicsUnit.Point, XPageDirection.Downwards);
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
                lines.Add(CreateRichLine(graphics, current, font, true));
                current.Clear();
                continue;
            }

            var item = new CellTextRun(element, snapshot[sourceIndex].Font);
            current.Add(item);
            if (wrap && availableWidth > 0 && current.Count > 1 && CreateRichLine(graphics, current, font, false).Width > availableWidth)
            {
                current.RemoveAt(current.Count - 1);
                lines.Add(CreateRichLine(graphics, current, font, false));
                current.Clear();
                current.Add(item);
            }
        }

        if (text.Length > 0)
        {
            lines.Add(CreateRichLine(graphics, current, font, false));
        }

        var result = new TextLayoutResult(new(lines.Count == 0 ? 0 : lines.Max(line => line.Width), lines.Sum(line => line.Height)), lines.AsReadOnly())
        {
            EffectiveFontSize = font.Size,
        };
        if (text.Length <= 2048)
        {
            if (richLayouts.Count >= 512)
            {
                richLayouts.Clear();
            }

            richLayouts[key] = result;
        }

        return result;
    }

    private TextLayoutLine CreateRichLine(XGraphics graphics, IReadOnlyList<CellTextRun> elements, FontStyle emptyFont, bool explicitBreak)
    {
        if (elements.Count == 0)
        {
            return CreateLine(graphics, string.Empty, explicitBreak, emptyFont, new(emptyFont.Family, emptyFont.Bold ? 700 : 400, emptyFont.Italic));
        }

        var segments = new List<CellTextRun>();
        foreach (var element in elements)
        {
            if (segments.Count > 0 && segments[segments.Count - 1].Font == element.Font)
            {
                segments[segments.Count - 1] = element with { Text = segments[segments.Count - 1].Text + element.Text };
            }
            else
            {
                segments.Add(element);
            }
        }

        var runs = new List<TextLayoutRun>();
        var x = 0d;
        var ascent = 0d;
        var descent = 0d;
        var leading = 0d;
        foreach (var segment in segments)
        {
            var style = segment.Font;
            var line = CreateLine(graphics, segment.Text, false, style, new(style.Family, style.Bold ? 700 : 400, style.Italic));
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
