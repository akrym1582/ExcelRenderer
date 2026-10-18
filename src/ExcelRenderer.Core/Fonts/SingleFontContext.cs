using System.Text;
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
    internal SingleFontContext(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ConversionMetrics.Report("fontSnapshotRead", 1);
        Validate(bytes);
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

    private static void Validate(byte[] bytes)
    {
        if (bytes.Length < 12 || Read32(bytes, 0) != 0x00010000)
        {
            throw new InvalidDataException("Only single static TrueType sfnt fonts are supported.");
        }

        var count = (bytes[4] << 8) | bytes[5];
        if (count == 0 || 12 + (count * 16) > bytes.Length)
        {
            throw new InvalidDataException("The font table directory is corrupt.");
        }

        var tables = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var position = 12 + (i * 16);
            var tag = Encoding.ASCII.GetString(bytes, position, 4);
            var offset = Read32(bytes, position + 8);
            var length = Read32(bytes, position + 12);
            if (offset > bytes.Length || length > bytes.Length - offset || !tables.Add(tag))
            {
                throw new InvalidDataException("The font contains an invalid table.");
            }
        }

        if (tables.Contains("fvar") || tables.Contains("CFF ") || tables.Contains("CFF2") ||
            new[] { "glyf", "loca", "head", "hhea", "hmtx", "maxp", "cmap", "name" }.Any(tag => !tables.Contains(tag)))
        {
            throw new InvalidDataException("A static TrueType outline font is required.");
        }
    }

    private static uint Read32(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
}
