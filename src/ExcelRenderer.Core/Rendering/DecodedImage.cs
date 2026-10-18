using SkiaSharp;

namespace ExcelRenderer.Core.Rendering;

/// <summary>Owns validated source pixels and optional original JPEG/PNG encoding.</summary>
internal sealed class DecodedImage(SKImage pixels, SKImage? encoded, bool directPdfJpeg, long estimatedBytes) : IDisposable
{
    /// <summary>Gets complete cached pixels without another codec pass.</summary>
    internal SKImage Raster => pixels;

    /// <summary>Gets eligible original JPEG data or validated native raster pixels for PDF encoding.</summary>
    internal SKImage PdfImage => directPdfJpeg ? encoded ?? pixels : pixels;

    /// <summary>Gets original PNG/JPEG data for SVG without another pixel decode.</summary>
    internal SKImage SvgImage => encoded ?? Raster;

    /// <summary>Gets decoded pixels and retained native encoded data.</summary>
    internal long EstimatedBytes { get; } = estimatedBytes;

    /// <inheritdoc/>
    public void Dispose()
    {
        encoded?.Dispose();
        pixels.Dispose();
    }
}
