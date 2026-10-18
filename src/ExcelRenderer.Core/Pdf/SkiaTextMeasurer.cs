using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Skia の物理フォントメトリクスを使用して、レイアウトに必要な文字列の寸法を測定します。</summary>
internal sealed class SkiaTextMeasurer : ITextMeasurer, ITextLayoutService
{
    private readonly SingleFontContext context;
    private readonly Dictionary<(string Text, FontStyle Font, double Width, bool Wrap), TextLayoutResult> _layouts = new();

    /// <summary>Initializes a new instance of the <see cref="SkiaTextMeasurer"/> class.</summary>
    /// <param name="context">The conversion font snapshot.</param>
    internal SkiaTextMeasurer(SingleFontContext context) => this.context = context;

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

        var key = (text, font, availableWidth, wrap);
        if (_layouts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var lineTexts = TextLineBreaker.Break(text, availableWidth, wrap, value => context.Face.Measure(context.Face.Typeface.GetGlyphs(value), font.Size));
        var lines = lineTexts.Select(line => CreateLine(line.Text, line.ExplicitBreak, font)).ToArray();
        var result = new TextLayoutResult(
            new(lines.Length == 0 ? 0 : lines.Max(line => line.Width), lines.Sum(line => line.Height)),
            Array.AsReadOnly(lines))
        {
            EffectiveFontSize = font.Size,
        };
        if (text.Length <= 2048)
        {
            if (_layouts.Count >= 512)
            {
                _layouts.Clear();
            }

            _layouts[key] = result;
        }

        return result;
    }

    private TextLayoutLine CreateLine(string text, bool explicitBreak, FontStyle style)
    {
        var (ascent, descent, leading) = context.Metrics(style.Size);
        var glyphs = context.Face.Typeface.GetGlyphs(text);
        return new(text, context.Face.Measure(glyphs, style.Size), ascent + descent + leading, ascent, explicitBreak)
        {
            Glyphs = new(context.Face, glyphs, text, text),
            Ascent = ascent,
            Descent = descent,
            Leading = leading,
        };
    }
}
