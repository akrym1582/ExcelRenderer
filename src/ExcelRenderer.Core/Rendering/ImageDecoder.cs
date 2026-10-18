using SkiaSharp;

namespace ExcelRenderer.Core.Rendering;

/// <summary>Validates complete pixels once while retaining eligible original vector encodings.</summary>
internal static class ImageDecoder
{
    /// <summary>Decodes complete pixels without accepting partially recovered images.</summary>
    /// <param name="bytes">The borrowed encoded bytes.</param>
    /// <param name="failure">The actual codec or pixel-decode failure.</param>
    /// <returns>The caller-owned decoded resource, or null.</returns>
    internal static DecodedImage? Decode(byte[] bytes, out string? failure)
    {
        failure = null;
        if (bytes.Length == 0)
        {
            failure = "Image data is empty.";
            return null;
        }

        try
        {
            using var stream = new SKMemoryStream(bytes);
            using var codec = SKCodec.Create(stream, out var created);
            if (codec is null)
            {
                failure = $"SKCodec.Create returned null ({created}); the image format may be unsupported or the data may be corrupt.";
                return null;
            }

            // Match the native encoded-image generator's raster representation. Converting
            // through unpremultiplied SKColor values would add another rounding step.
            var info = codec.Info;
            if (info.AlphaType == SKAlphaType.Unpremul)
            {
                info.AlphaType = SKAlphaType.Premul;
            }

            using var bitmap = new SKBitmap(info);
            ConversionMetrics.Report("imagePixelDecode", 1);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success)
            {
                failure = $"SKCodec.GetPixels returned {result} (format={codec.EncodedFormat}, size={info.Width}x{info.Height}); complete pixels are required.";
                return null;
            }

            bitmap.SetImmutable();
            var raster = Orient(bitmap, info, codec.EncodedOrigin);
            SKImage? encoded = null;
            try
            {
                if (codec.EncodedFormat is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png)
                {
                    encoded = SKImage.FromEncodedData(bytes);
                }

                return new(
                    raster,
                    encoded,
                    codec.EncodedFormat == SKEncodedImageFormat.Jpeg && codec.EncodedOrigin == SKEncodedOrigin.TopLeft,
                    ((long)info.RowBytes * info.Height) + (encoded is null ? 0 : bytes.Length));
            }
            catch
            {
                encoded?.Dispose();
                raster.Dispose();
                throw;
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            failure = $"Image pixel decode failed: {error.GetType().Name}: {error.Message}";
            return null;
        }
    }

    private static SKImage Orient(SKBitmap source, SKImageInfo info, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return SKImage.FromBitmap(source);
        }

        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = info.Width;
        var height = info.Height;
        info.Width = swapped ? height : width;
        info.Height = swapped ? width : height;
        using var bitmap = new SKBitmap(info);
        var input = source.GetPixelSpan();
        var output = bitmap.GetPixelSpan();
        var pixelBytes = info.BytesPerPixel;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (dx, dy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (width - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (width - 1 - x, height - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, height - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (height - 1 - y, x),
                    SKEncodedOrigin.RightBottom => (height - 1 - y, width - 1 - x),
                    SKEncodedOrigin.LeftBottom => (y, width - 1 - x),
                    _ => throw new InvalidDataException("Unknown encoded image orientation."),
                };
                input.Slice((y * source.RowBytes) + (x * pixelBytes), pixelBytes)
                    .CopyTo(output.Slice((dy * bitmap.RowBytes) + (dx * pixelBytes), pixelBytes));
            }
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }
}
