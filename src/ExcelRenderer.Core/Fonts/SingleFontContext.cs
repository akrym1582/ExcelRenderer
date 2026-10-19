using ExcelRenderer.Core.Fonts.TrueType;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Core.Fonts;

/// <summary>Owns one validated, immutable static TrueType font snapshot.</summary>
internal sealed class SingleFontContext : IDisposable
{
    /// <summary>The only family used by reader, layout, and renderer.</summary>
    internal const string Family = "CoreSingleFont";
    private readonly SKTypeface typeface;

    /// <summary>Initializes a new instance of the <see cref="SingleFontContext"/> class.</summary>
    /// <param name="path">The static TrueType file.</param>
    /// <param name="collectionIndex">The zero-based collection face index.</param>
    /// <param name="token">The conversion cancellation token.</param>
    internal SingleFontContext(string path, int collectionIndex = 0, CancellationToken token = default)
    {
        var bytes = ReadFace(path, collectionIndex, token);
        ConversionMetrics.Report("fontSnapshotRead", 1);
        Face = new SkiaFontFace(bytes);
        typeface = Face.Typeface;
    }

    /// <summary>Gets the immutable font snapshot shared by measurement, subset and drawing.</summary>
    internal SkiaFontFace Face { get; }

    /// <inheritdoc/>
    public void Dispose() => Face.Dispose();

    /// <summary>Gets line metrics from the same snapshot used by drawing.</summary>
    /// <param name="size">The point size.</param>
    /// <returns>The ascent, descent and leading.</returns>
    internal (double Ascent, double Descent, double Leading) Metrics(double size) => Face.Metrics(size);

    /// <summary>Measures Normal-style maximum digit width at 96 DPI.</summary>
    /// <param name="size">The Normal font point size.</param>
    /// <returns>The rounded maximum width in pixels.</returns>
    internal double MaximumDigitWidth(double size)
    {
        using var font = new SKFont(typeface, (float)(size * 96 / 72));
        return Math.Max(1, Math.Round(Enumerable.Range(0, 10).Max(digit => font.MeasureText(digit.ToString())), MidpointRounding.AwayFromZero));
    }

    private static byte[] ReadFace(string path, int collectionIndex, CancellationToken token)
    {
        try
        {
            var bytes = FontFaceExtractor.Extract(File.ReadAllBytes(path), collectionIndex, token);
            FontCapabilities.ValidatePdf(new SfntReader(bytes, token: token), token);
            return bytes;
        }
        catch (NotSupportedException error)
        {
            // Core has historically reported unsupported single-font inputs as invalid data.
            throw new InvalidDataException($"Font '{path}', face {collectionIndex}: {error.Message}", error);
        }
    }
}
