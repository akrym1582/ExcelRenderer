using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

public sealed class PdfSharpTextMeasurer : ITextMeasurer
{
    public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap)
    {
        if (string.IsNullOrEmpty(text)) return new(0, 0);
        using var graphics = XGraphics.CreateMeasureContext(new XSize(availableWidth, double.MaxValue), XGraphicsUnit.Point, XPageDirection.Downwards);
        var size = graphics.MeasureString(text, CreateFont(font));
        if (!wrap || size.Width <= availableWidth) return new(size.Width, size.Height);
        var lines = Math.Ceiling(size.Width / Math.Max(availableWidth, 1));
        return new(availableWidth, size.Height * lines);
    }

    internal static XFont CreateFont(FontStyle font)
    {
        var style = XFontStyleEx.Regular;
        if (font.Bold) style |= XFontStyleEx.Bold;
        if (font.Italic) style |= XFontStyleEx.Italic;
        if (font.Underline) style |= XFontStyleEx.Underline;
        return new XFont(font.Family, font.Size, style);
    }
}
