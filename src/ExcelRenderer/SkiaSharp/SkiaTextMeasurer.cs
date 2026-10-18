using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Skia の物理フォントメトリクスを使用して、レイアウトに必要な文字列の寸法を測定します。</summary>
public sealed partial class SkiaTextMeasurer : ITextMeasurer, ITextLayoutService, IRichTextLayoutService
{
    private readonly IFontManager _fontManager;
    private readonly object standaloneSync = new();
    private readonly bool borrowConversionScope;
    private readonly Dictionary<(string Text, FontStyle Font, double Width, bool Wrap, int Generation), TextLayoutResult>? _layouts;

    /// <summary>Initializes a new instance of the <see cref="SkiaTextMeasurer"/> class.</summary>
    public SkiaTextMeasurer()
        : this(new FontManager(new FontOptions()))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SkiaTextMeasurer"/> class.</summary>
    /// <param name="fontManager">Font manager used by output rendering.</param>
    public SkiaTextMeasurer(IFontManager fontManager)
    {
        _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));
    }

    /// <summary>Initializes a new instance of the <see cref="SkiaTextMeasurer"/> class with a conversion-local cache.</summary>
    /// <param name="fontManager">The conversion font manager.</param>
    /// <param name="cacheLayouts">Whether to cache immutable finalized layouts.</param>
    internal SkiaTextMeasurer(IFontManager fontManager, bool cacheLayouts)
        : this(fontManager)
    {
        _layouts = cacheLayouts ? new() : null;
        borrowConversionScope = true;
    }

    private bool BorrowsResources => borrowConversionScope || ReferenceEquals(Rendering.RenderResourceSession.Current?.TextMeasurer, this);

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
        if (borrowConversionScope)
        {
            return LayoutCore(text, font, availableWidth, wrap);
        }

        lock (standaloneSync)
        {
            return LayoutCore(text, font, availableWidth, wrap);
        }
    }

    private static double MeasureRun(TextRun run, FontStyle style)
    {
        var face = ConversionFontResources.Current!.GetFace(run.Font);
        var glyphs = (run.GlyphId ?? run.ColorEmojiGlyphId) is { } glyph ? new[] { glyph } : face.Typeface.GetGlyphs(run.Text);
        return face.Measure(glyphs, style.Size);
    }

    private static (double Ascent, double Descent, double Leading) MeasureLineMetrics(ResolvedFont font, double size) =>
        ConversionFontResources.Current!.GetFace(font).Metrics(size);

    private TextLayoutResult LayoutCore(string text, FontStyle font, double availableWidth, bool wrap)
    {
        using var resources = !BorrowsResources || ConversionFontResources.Current is null ? new ConversionFontResources() : null;
        if (string.IsNullOrEmpty(text))
        {
            return new(new(0, 0), []) { EffectiveFontSize = font.Size };
        }

        var key = (text, font, availableWidth, wrap, (_fontManager as FontManager)?.RegistrationGeneration ?? 0);
        if (_layouts?.TryGetValue(key, out var cached) == true)
        {
            return cached;
        }

        var request = new FontRequest(font.Family, font.Bold ? 700 : 400, font.Italic);
        var lineTexts = Core.Layout.TextLineBreaker.Break(text, availableWidth, wrap, value => MeasureWidth(value, font, request));
        var lines = lineTexts.Select(line => CreateLine(line.Text, line.ExplicitBreak, font, request)).ToArray();
        var result = new TextLayoutResult(
            new(lines.Length == 0 ? 0 : lines.Max(line => line.Width), lines.Sum(line => line.Height)),
            Array.AsReadOnly(lines))
        {
            EffectiveFontSize = font.Size,
        };
        if (_layouts is not null && text.Length <= 2048)
        {
            if (_layouts.Count >= 512)
            {
                _layouts.Clear();
            }

            _layouts[key] = result;
        }

        return result;
    }

    private TextLayoutLine CreateLine(
        string text,
        bool explicitBreak,
        FontStyle style,
        FontRequest request,
        bool measureOnly = false)
    {
        if (measureOnly)
        {
            return new(text, MeasureWidth(text, style, request), 0, 0, [], explicitBreak);
        }

        var resolvedRuns = _fontManager?.ResolveTextRuns(text, request) ?? [];
        var positionedRuns = new List<TextLayoutRun>();
        var x = 0d;
        foreach (var run in resolvedRuns)
        {
            var advance = MeasureRun(run, style);
            positionedRuns.Add(ResolvedGlyphLayout.Finalize(new(run, x, advance)));
            x += advance;
        }

        var primary = _fontManager?.Resolve(request);
        var metrics = resolvedRuns.Select(run => run.Font)
            .Concat(primary is null ? [] : [primary])
            .GroupBy(face => face.FaceId)
            .Select(group => MeasureLineMetrics(group.First(), style.Size))
            .ToArray();
        var fallbackHeight = 0d;
        var ascent = metrics.Length == 0 ? fallbackHeight * 0.8 : metrics.Max(item => item.Ascent);
        var descent = metrics.Length == 0 ? fallbackHeight - ascent : metrics.Max(item => item.Descent);
        var leading = metrics.Length == 0 ? 0 : metrics.Max(item => item.Leading);
        var height = ascent + descent + leading;
        var width = resolvedRuns.Count == 0 ? 0 : x;
        return new(text, width, height, ascent, positionedRuns.AsReadOnly(), explicitBreak)
        {
            Ascent = ascent,
            Descent = descent,
            Leading = leading,
        };
    }

    private double MeasureWidth(string text, FontStyle style, FontRequest request)
    {
        return _fontManager.ResolveTextRuns(text, request).Sum(run => MeasureRun(run, style));
    }
}
