using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Freezes the established PNG/SVG native measurement and float rounding before painting.</summary>
internal sealed class SkiaCompatibilityTextLayout(IFontManager? manager) : ICommandTextLayoutService
{
    private readonly Dictionary<FontRequest, ResolvedFont> systemFonts = [];

    /// <inheritdoc/>
    public TextLayoutResult Layout(string text, FontStyle font, double availableWidth, bool wrap) =>
        Layout(new(1, new(0, 0, availableWidth, 0), text, CellStyle.Default with { Font = font, WrapText = wrap }));

    /// <inheritdoc/>
    public TextLayoutResult Layout(DrawTextCommand command)
    {
        var style = command.Style;
        var request = new FontRequest(style.Font.Family, style.Font.Bold ? 700 : 400, style.Font.Italic);
        var primary = manager?.Resolve(request) ?? ResolveSystem(request);
        var face = ConversionFontResources.Current!.GetTypeface(primary);
        using var font = new SKFont(face, (float)style.Font.Size);
        using var paint = new SKPaint { IsAntialias = true };
        if (style.ShrinkToFit && !style.WrapText && command.Bounds.Width > 0)
        {
            var widest = command.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Max(Measure);
            if (widest > command.Bounds.Width)
            {
                font.Size *= (float)(command.Bounds.Width / widest);
            }
        }

        var broken = Core.Layout.TextLineBreaker.Break(command.Text, command.Bounds.Width, style.WrapText, value => Measure(value)).ToArray();
        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var textHeight = lineHeight * broken.Length;
        var top = style.VerticalAlignment switch
        {
            VerticalAlignment.Center => command.Bounds.Y + ((command.Bounds.Height - textHeight) / 2),
            VerticalAlignment.Bottom => command.Bounds.Y + command.Bounds.Height - textHeight,
            _ => command.Bounds.Y,
        };
        var baseline = (float)top - metrics.Ascent;
        var lines = new TextLayoutLine[broken.Length];
        for (var index = 0; index < broken.Length; index++)
        {
            var (text, explicitBreak) = broken[index];
            var width = Measure(text);
            var left = style.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => command.Bounds.X + ((command.Bounds.Width - width) / 2),
                HorizontalAlignment.Right => command.Bounds.X + command.Bounds.Width - width,
                _ => command.Bounds.X,
            };
            var x = (float)left;
            var runs = new List<TextLayoutRun>();
            foreach (var run in Resolve(text))
            {
                var advance = MeasureRun(run);
                runs.Add(ResolvedGlyphLayout.Finalize(new(run, x - left, advance)));
                x += advance;
            }

            lines[index] = new(text, width, lineHeight, baseline - top, runs.AsReadOnly(), explicitBreak)
            {
                Ascent = -metrics.Ascent,
                Descent = metrics.Descent,
                Leading = metrics.Leading,
            };
            baseline += lineHeight;
            top += lineHeight;
        }

        return new(new(lines.Length == 0 ? 0 : lines.Max(line => line.Width), textHeight), Array.AsReadOnly(lines)) { EffectiveFontSize = font.Size };

        IReadOnlyList<TextRun> Resolve(string text) => manager?.ResolveTextRuns(text, request) ?? [new(text, primary)];

        float Measure(string text)
        {
            if (manager is null)
            {
                return font.MeasureText(text, paint);
            }

            var width = 0f;
            foreach (var run in Resolve(text))
            {
                width += MeasureRun(run);
            }

            return width;
        }

        float MeasureRun(TextRun run)
        {
            using var runFont = new SKFont(ConversionFontResources.Current!.GetTypeface(run.Font), font.Size);
            return (run.GlyphId ?? run.ColorEmojiGlyphId) is { } glyph ? runFont.GetGlyphWidths([glyph])[0] : runFont.MeasureText(run.Text, paint);
        }
    }

    /// <summary>Tests whether an internal replay may borrow this exact caller's font policy.</summary>
    /// <param name="fonts">The renderer's explicitly supplied policy.</param>
    /// <returns>Whether the resolver instance is identical.</returns>
    internal bool Uses(IFontManager? fonts) => ReferenceEquals(manager, fonts);

    private ResolvedFont ResolveSystem(FontRequest request)
    {
        if (systemFonts.TryGetValue(request, out var cached))
        {
            return cached;
        }

        using var selected = SKTypeface.FromFamilyName(request.Family, request.Weight >= 700 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, request.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var face = selected ?? SKTypeface.Default;
        using var stream = face.OpenStream(out var collectionIndex);
        using var data = SKData.Create(stream);
        var result = new ResolvedFont(face.FamilyName, request.Weight, request.Italic, string.Empty)
        {
            FontData = data.ToArray(),
            FontCollectionIndex = collectionIndex,
        };
        if (systemFonts.Count >= 64)
        {
            systemFonts.Clear();
        }

        systemFonts.Add(request, result);
        return result;
    }
}
