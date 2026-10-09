using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ClosedXML.Graphics;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Model;
using PdfSharp.Drawing;
using SkiaSharp;

namespace ExcelRenderer.Core.Excel;

/// <summary>Prevents ClosedXML from exploring OS fonts or introducing a second face.</summary>
internal sealed class SingleFontGraphicEngine(SingleFontContext context) : IXLGraphicEngine
{
    /// <inheritdoc/>
    public XLPictureInfo GetPictureInfo(Stream imageStream, XLPictureFormat expectedFormat)
    {
        using var stream = new SKManagedStream(imageStream, false);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
        {
            // Keep the bytes and placement so the PDF decoder can return a nonfatal diagnostic.
            return new(expectedFormat == XLPictureFormat.Unknown ? XLPictureFormat.Png : expectedFormat, 1, 1, 96, 96);
        }

        var format = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => XLPictureFormat.Jpeg,
            SKEncodedImageFormat.Gif => XLPictureFormat.Gif,
            SKEncodedImageFormat.Bmp => XLPictureFormat.Bmp,
            _ => expectedFormat == XLPictureFormat.Unknown ? XLPictureFormat.Png : expectedFormat,
        };
        return new(format, (uint)codec.Info.Width, (uint)codec.Info.Height, 96, 96);
    }

    /// <inheritdoc/>
    public double GetTextHeight(IXLFontBase font, double dpi)
    {
        var (ascent, descent, _) = context.Metrics(font.FontSize);
        return (ascent + descent) * dpi / 72;
    }

    /// <inheritdoc/>
    public double GetTextWidth(string text, IXLFontBase font, double dpi)
    {
        using var graphics = XGraphics.CreateMeasureContext(new XSize(double.MaxValue, double.MaxValue), XGraphicsUnit.Point, XPageDirection.Downwards);
        return graphics.MeasureString(DisplayText.WithoutVariationSelectors(text), context.GetPdfFont(font.FontSize)).Width * dpi / 72;
    }

    /// <inheritdoc/>
    public double GetMaxDigitWidth(IXLFontBase font, double dpi) => context.MaximumDigitWidth(font.FontSize * dpi / 96);

    /// <inheritdoc/>
    public double GetDescent(IXLFontBase font, double dpi) => context.Metrics(font.FontSize).Descent * dpi / 72;

    /// <inheritdoc/>
    public GlyphBox GetGlyphBox(ReadOnlySpan<int> graphemeCluster, IXLFontBase font, Dpi dpi)
    {
        var text = string.Concat(graphemeCluster.ToArray().Select(char.ConvertFromUtf32));
        return new((float)GetTextWidth(text, font, dpi.X), (float)(font.FontSize * dpi.Y / 72), (float)GetDescent(font, dpi.Y));
    }
}
