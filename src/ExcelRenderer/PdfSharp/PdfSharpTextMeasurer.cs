using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

/// <summary>PDFsharp のフォントメトリクスを使用して、レイアウトに必要な文字列の寸法を測定します。</summary>
public sealed class PdfSharpTextMeasurer : ITextMeasurer
{
    /// <summary>指定したフォントで文字列を測定し、必要に応じて利用可能幅に収まる行数へ折り返した寸法を算出します。</summary>
    /// <param name="text">寸法を測定する文字列です。</param>
    /// <param name="font">測定に使用するフォントファミリー、サイズ、および装飾です。</param>
    /// <param name="availableWidth">折り返し後の一行に利用できる幅をポイント単位で指定します。</param>
    /// <param name="wrap">利用可能幅を超える文字列の高さを複数行分として算出する場合は <see langword="true"/> です。</param>
    /// <returns>折り返さない場合は文字列本来の幅と高さ、折り返す場合は利用可能幅と推定した全行の高さを返します。空文字列の場合は幅と高さがともに 0 です。</returns>
    public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new(0, 0);
        }

        using var graphics = XGraphics.CreateMeasureContext(new XSize(availableWidth, double.MaxValue), XGraphicsUnit.Point, XPageDirection.Downwards);
        var size = graphics.MeasureString(text, CreateFont(font));
        if (!wrap || size.Width <= availableWidth)
        {
            return new(size.Width, size.Height);
        }

        var lines = Math.Ceiling(size.Width / Math.Max(availableWidth, 1));
        return new(availableWidth, size.Height * lines);
    }

    /// <summary>レンダリング用フォント書式を、同じファミリー、サイズ、太字、斜体、および下線を持つ PDFsharp フォントへ変換します。</summary>
    /// <param name="font">PDFsharp フォントへ反映するレンダリング用フォント書式です。</param>
    /// <returns>指定された書式属性を持つ PDFsharp のフォントを返します。</returns>
    internal static XFont CreateFont(FontStyle font)
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

        if (font.Underline)
        {
            style |= XFontStyleEx.Underline;
        }

        return new XFont(font.Family, font.Size, style);
    }
}
