using SkiaSharp;

namespace ExcelRenderer.Core.Rendering;

/// <summary>Shares native image decoding and actionable failure details between products.</summary>
internal static class ImageDecoder
{
    /// <summary>Decodes encoded image bytes without transferring ownership of the input.</summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <returns>The caller-owned image, or null for unsupported/corrupt data.</returns>
    internal static SKImage? Decode(byte[] bytes)
    {
        try
        {
            return SKImage.FromEncodedData(bytes);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Diagnoses a failed decode without changing or reencoding the input.</summary>
    /// <param name="bytes">The failed encoded bytes.</param>
    /// <returns>The decoder reason.</returns>
    internal static string DescribeFailure(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "Image data is empty.";
        }

        using var encoded = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(encoded, out var result);
        return codec is null
            ? $"SKCodec.Create returned null ({result}); the image format may be unsupported or the data may be corrupt."
            : $"SKImage.FromEncodedData returned null (format={codec.EncodedFormat}, size={codec.Info.Width}x{codec.Info.Height}); the image data could not be decoded.";
    }
}
