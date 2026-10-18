using System.Security.Cryptography;
using SkiaSharp;

namespace ExcelRenderer.Core.Fonts;

/// <summary>Owns an immutable physical font snapshot and unhinted point metrics.</summary>
internal sealed class SkiaFontFace : IDisposable
{
    private readonly byte[] head;
    private readonly byte[] hhea;
    private readonly byte[] hmtx;
    private readonly Dictionary<double, (double Ascent, double Descent, double Leading)> metrics = new();

    /// <summary>Initializes a new instance of the <see cref="SkiaFontFace"/> class.</summary>
    /// <param name="bytes">Font data to snapshot.</param>
    /// <param name="identity">The already computed content identity.</param>
    internal SkiaFontFace(byte[] bytes, string? identity = null)
    {
        Data = (byte[])bytes.Clone();
        Identity = identity ?? ContentIdentity(Data);
        using var data = SKData.CreateCopy(Data);
        Typeface = SKTypeface.FromData(data) ?? throw new InvalidDataException("Skia could not load the font snapshot.");
        var tags = Typeface.GetTableTags();
        Color = tags.Contains(0x43424454u) || tags.Contains(0x434f4c52u) || tags.Contains(0x73626978u);
        head = Typeface.GetTableData(0x68656164);
        hhea = Typeface.GetTableData(0x68686561);
        hmtx = Typeface.GetTableData(0x686d7478);
        if (head.Length < 20 || hhea.Length < 36 || Units == 0 || MetricCount == 0 || hmtx.Length < MetricCount * 4)
        {
            Typeface.Dispose();
            throw new InvalidDataException("The font requires valid sfnt head/hhea/hmtx advance metrics.");
        }

        Rendering.ConversionMetrics.Report("typefaceCreated", 1);
    }

    /// <summary>Gets the snapshot bytes, borrowed by internal subset operations.</summary>
    internal byte[] Data { get; }

    /// <summary>Gets the content identity.</summary>
    internal string Identity { get; }

    /// <summary>Gets a value indicating whether native color strike metrics are required.</summary>
    internal bool Color { get; }

    /// <summary>Gets the borrowed physical typeface.</summary>
    internal SKTypeface Typeface { get; }

    private int Units => (head[18] << 8) | head[19];

    private int MetricCount => (hhea[34] << 8) | hhea[35];

    /// <inheritdoc/>
    public void Dispose()
    {
        Typeface.Dispose();
        metrics.Clear();
        Rendering.ConversionMetrics.Report("typefaceDisposed", 1);
    }

    /// <summary>Identifies actual font bytes independently of requested family or caller labels.</summary>
    /// <param name="bytes">The physical face bytes.</param>
    /// <returns>The content hash.</returns>
    internal static string ContentIdentity(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    /// <summary>Creates an explicitly configured unhinted font.</summary>
    /// <param name="face">The borrowed face.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>The caller-owned font.</returns>
    internal static SKFont CreateFont(SKTypeface face, double size) => new(face, (float)size)
    {
        LinearMetrics = true,
        Subpixel = true,
        Hinting = SKFontHinting.None,
    };

    /// <summary>Measures advances in design units without native float quantization or kerning.</summary>
    /// <param name="glyphs">The already selected glyphs.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>The advance in points.</returns>
    internal double Measure(IEnumerable<ushort> glyphs, double size)
    {
        if (Color)
        {
            // Color strikes use the same normal native metrics as their drawing font.
            // Their bitmap strike advances are distinct from scalable outline advances.
            using var font = new SKFont(Typeface, (float)size) { LinearMetrics = false, Subpixel = false, Hinting = SKFontHinting.Normal };
            return font.GetGlyphWidths(glyphs as ushort[] ?? glyphs.ToArray()).Sum(width => (double)width);
        }

        long total = 0;
        foreach (var glyph in glyphs)
        {
            total += AdvanceUnits(glyph);
        }

        return total * size / Units;
    }

    /// <summary>Gets per-glyph advances without allocating a collection or native font per glyph.</summary>
    /// <param name="glyphs">The selected glyphs.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>Individual point advances under the existing scalable/color policy.</returns>
    internal double[] Advances(ushort[] glyphs, double size)
    {
        var result = new double[glyphs.Length];
        if (Color)
        {
            using var font = new SKFont(Typeface, (float)size) { LinearMetrics = false, Subpixel = false, Hinting = SKFontHinting.Normal };
            var widths = font.GetGlyphWidths(glyphs);
            for (var index = 0; index < glyphs.Length; index++)
            {
                result[index] = widths[index];
            }
        }
        else
        {
            for (var index = 0; index < glyphs.Length; index++)
            {
                result[index] = AdvanceUnits(glyphs[index]) * size / Units;
            }
        }

        return result;
    }

    /// <summary>Gets glyph origins from the same advances used by layout.</summary>
    /// <param name="glyphs">The selected glyphs.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>Point-coordinate positions relative to the run.</returns>
    internal SKPoint[] Positions(ushort[] glyphs, double size)
    {
        var result = new SKPoint[glyphs.Length];
        var colorAdvances = Color ? Advances(glyphs, size) : null;
        double x = 0;
        for (var index = 0; index < glyphs.Length; index++)
        {
            result[index] = new((float)x, 0);
            x += colorAdvances is null ? AdvanceUnits(glyphs[index]) * size / Units : colorAdvances[index];
        }

        return result;
    }

    /// <summary>Gets positive line metrics from the immutable face.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>The ascent, descent and leading.</returns>
    internal (double Ascent, double Descent, double Leading) Metrics(double size)
    {
        if (!metrics.TryGetValue(size, out var result))
        {
            using var font = CreateFont(Typeface, size);
            var value = font.Metrics;
            result = (-value.Ascent, value.Descent, value.Leading);
            metrics.Add(size, result);
            Rendering.ConversionMetrics.Report("lineMetricsComputed", 1);
        }

        return result;
    }

    private int AdvanceUnits(ushort glyph)
    {
        var offset = Math.Min(glyph, MetricCount - 1) * 4;
        return (hmtx[offset] << 8) | hmtx[offset + 1];
    }
}
