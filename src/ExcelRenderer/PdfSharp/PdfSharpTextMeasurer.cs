using System.Globalization;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

/// <summary>PDFsharp のフォントメトリクスを使用して、レイアウトに必要な文字列の寸法を測定します。</summary>
public sealed class PdfSharpTextMeasurer : ITextMeasurer, ITextLayoutService
{
    private readonly IFontManager? _fontManager;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpTextMeasurer"/> class.</summary>
    public PdfSharpTextMeasurer()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfSharpTextMeasurer"/> class.</summary>
    /// <param name="fontManager">Font manager used by output rendering.</param>
    public PdfSharpTextMeasurer(IFontManager fontManager)
    {
        _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));
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
        if (string.IsNullOrEmpty(text))
        {
            return new(new(0, 0), []) { EffectiveFontSize = font.Size };
        }

        using var graphics = XGraphics.CreateMeasureContext(new XSize(availableWidth, double.MaxValue), XGraphicsUnit.Point, XPageDirection.Downwards);
        var request = new FontRequest(font.Family, font.Bold ? 700 : 400, font.Italic);
        var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lineTexts = new List<(string Text, bool ExplicitBreak)>();
        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
        {
            var paragraph = paragraphs[paragraphIndex];
            var explicitBreak = paragraphIndex < paragraphs.Length - 1;
            if (!wrap || availableWidth <= 0)
            {
                lineTexts.Add((paragraph, explicitBreak));
                continue;
            }

            var current = string.Empty;
            foreach (var element in EnumerateTextElements(paragraph))
            {
                var candidate = current + element;
                if (current.Length > 0 && MeasureWidth(graphics, candidate, font, request) > availableWidth)
                {
                    lineTexts.Add((current, false));
                    current = element;
                }
                else
                {
                    current = candidate;
                }
            }

            lineTexts.Add((current, explicitBreak));
        }

        var lines = lineTexts.Select(line => CreateLine(graphics, line.Text, line.ExplicitBreak, font, request)).ToArray();
        return new(
            new(lines.Length == 0 ? 0 : lines.Max(line => line.Width), lines.Sum(line => line.Height)),
            lines)
        {
            EffectiveFontSize = font.Size,
        };
    }

    /// <summary>レンダリング用フォント書式を、同じファミリー、サイズ、太字、斜体、および下線を持つ PDFsharp フォントへ変換します。</summary>
    /// <param name="font">PDFsharp フォントへ反映するレンダリング用フォント書式です。</param>
    /// <param name="includeUnderline">下線属性を PDFsharp フォント自体へ含める場合は <see langword="true"/> です。</param>
    /// <returns>指定された書式属性を持つ PDFsharp のフォントを返します。</returns>
    internal static XFont CreateFont(FontStyle font, bool includeUnderline = true)
    {
        var style = XFontStyleEx.Regular;
        if (font.Bold)
        {
            style |= XFontStyleEx.Bold;
        }

        if (font.Italic)
        {
            style |= XFontStyleEx.Italic;
        }

        if (includeUnderline && font.Underline)
        {
            style |= XFontStyleEx.Underline;
        }

        return new XFont(font.Family, font.Size, style);
    }

    /// <summary>Creates a PDF font bound to an already selected physical face.</summary>
    /// <param name="font">The selected physical face.</param>
    /// <param name="size">The font size in points.</param>
    /// <returns>A PDFsharp font that uses exactly the supplied face.</returns>
    internal static XFont CreateResolvedFont(ResolvedFont font, double size) =>
        new(PdfSharpFontResolver.RegisterResolvedFont(font), size, XFontStyleEx.Regular);

    private static double MeasureRun(XGraphics graphics, TextRun run, FontStyle style)
    {
        if ((run.GlyphId ?? run.ColorEmojiGlyphId) is not { } glyph && !run.MissingPrivateUseGlyph)
        {
            return graphics.MeasureString(run.Text, CreateResolvedFont(run.Font, style.Size)).Width;
        }

        using Stream stream = run.Font.FontData is null ? File.OpenRead(run.Font.FilePath) : new MemoryStream(run.Font.FontData, false);
        using var typeface = SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"フォント {run.Font.Family} を読み込めません。");
        using var skFont = new SKFont(typeface, (float)style.Size);
        glyph = run.GlyphId ?? run.ColorEmojiGlyphId ?? skFont.GetGlyphs(run.Text)[0];
        return skFont.GetGlyphWidths([glyph])[0];
    }

    private static (double Ascent, double Descent, double Leading) MeasureLineMetrics(ResolvedFont font, double size)
    {
        using Stream stream = font.FontData is null ? File.OpenRead(font.FilePath) : new MemoryStream(font.FontData, false);
        using var typeface = SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"フォント {font.Family} を読み込めません。");
        using var skFont = new SKFont(typeface, (float)size);
        var metrics = skFont.Metrics;
        return (-metrics.Ascent, metrics.Descent, metrics.Leading);
    }

    private TextLayoutLine CreateLine(
        XGraphics graphics,
        string text,
        bool explicitBreak,
        FontStyle style,
        FontRequest request)
    {
        var resolvedRuns = _fontManager?.ResolveTextRuns(text, request) ?? [];
        var positionedRuns = new List<TextLayoutRun>();
        var x = 0d;
        foreach (var run in resolvedRuns)
        {
            var advance = MeasureRun(graphics, run, style);
            positionedRuns.Add(new(run, x, advance));
            x += advance;
        }

        var primary = _fontManager?.Resolve(request);
        var metrics = resolvedRuns.Select(run => MeasureLineMetrics(run.Font, style.Size))
            .Concat(primary is null ? [] : [MeasureLineMetrics(primary, style.Size)])
            .ToArray();
        var fallbackHeight = metrics.Length == 0 ? graphics.MeasureString("Ag", CreateFont(style)).Height : 0;
        var ascent = metrics.Length == 0 ? fallbackHeight * 0.8 : metrics.Max(item => item.Ascent);
        var descent = metrics.Length == 0 ? fallbackHeight - ascent : metrics.Max(item => item.Descent);
        var leading = metrics.Length == 0 ? 0 : metrics.Max(item => item.Leading);
        var height = ascent + descent + leading;
        var width = resolvedRuns.Count == 0 ? graphics.MeasureString(text, CreateFont(style)).Width : x;
        return new(text, width, height, ascent, positionedRuns, explicitBreak)
        {
            Ascent = ascent,
            Descent = descent,
            Leading = leading,
        };
    }

    private double MeasureWidth(XGraphics graphics, string text, FontStyle style, FontRequest request)
    {
        var runs = _fontManager?.ResolveTextRuns(text, request);
        return runs is null
            ? graphics.MeasureString(text, CreateFont(style)).Width
            : runs.Sum(run => MeasureRun(graphics, run, style));
    }

    private IEnumerable<string> EnumerateTextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }
}
