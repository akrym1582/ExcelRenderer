using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Fonts.TrueType;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Owns one subset per immutable physical face and preserves source Unicode clusters.</summary>
internal sealed class PdfFontCollection : IDisposable
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly CancellationToken token;
    private readonly bool fullEmbed;
    private bool prepared;

    /// <summary>Initializes a new instance of the <see cref="PdfFontCollection"/> class.</summary>
    /// <param name="token">The conversion token.</param>
    /// <param name="fullEmbed">Comparison-only full embedding control.</param>
    internal PdfFontCollection(CancellationToken token, bool fullEmbed = false)
    {
        this.token = token;
        this.fullEmbed = fullEmbed;
    }

    /// <summary>Gets a value indicating whether usage is being collected.</summary>
    internal bool IsCollecting => !prepared;

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var entry in entries.Values)
        {
            foreach (var font in entry.Sizes.Values)
            {
                font.Dispose();
                ConversionMetrics.Report("pdfFontDisposed", 1);
            }

            if (entry.Subset is not null)
            {
                entry.Subset.Dispose();
                ConversionMetrics.Report("pdfSubsetTypefaceDisposed", 1);
            }
        }

        entries.Clear();
    }

    /// <summary>Prepares shared subsets after all selected pages have been collected.</summary>
    internal void Prepare()
    {
        foreach (var entry in entries.Values)
        {
            token.ThrowIfCancellationRequested();
            FontCapabilities.ValidatePdf(new SfntReader(entry.Face.Data, token: token), token);
            if (entry.Color || fullEmbed)
            {
                continue;
            }

            var bytes = ConversionMetrics.Measure("pdfSubset", () => TrueTypeSubsetter.Create(entry.Face.Data, entry.Glyphs, token));
            ConversionMetrics.Report("pdfOriginalFontBytes", entry.Face.Data.Length);
            ConversionMetrics.Report("pdfSubsetFontBytes", bytes.Length);
            using var data = SKData.CreateCopy(bytes);
            entry.Subset = SKTypeface.FromData(data) ?? throw new InvalidDataException("Skia could not load the subset font.");
            ConversionMetrics.Report("pdfSubsetTypefaceCreated", 1);
        }

        prepared = true;
    }

    /// <summary>Collects or paints selected glyphs without fallback, shaping or wrapping.</summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="face">The immutable physical face.</param>
    /// <param name="glyphs">The selected glyph IDs.</param>
    /// <param name="source">The original UTF-8 Unicode.</param>
    /// <param name="clusters">The source byte offset per glyph.</param>
    /// <param name="size">The point size.</param>
    /// <param name="x">The finalized X.</param>
    /// <param name="y">The finalized baseline.</param>
    /// <param name="paint">The text paint.</param>
    internal void Draw(SKCanvas canvas, SkiaFontFace face, ushort[] glyphs, byte[] source, uint[] clusters, float size, float x, float y, SKPaint paint)
    {
        token.ThrowIfCancellationRequested();
        if (!entries.TryGetValue(face.Identity, out var entry))
        {
            if (prepared)
            {
                throw new InvalidOperationException("Skia PDF encountered an uncollected physical face.");
            }

            entry = new(face);
            entries.Add(face.Identity, entry);
        }

        if (!prepared)
        {
            entry.Glyphs.UnionWith(glyphs);
            return;
        }

        if (glyphs.Any(glyph => !entry.Glyphs.Contains(glyph)))
        {
            throw new InvalidOperationException("Skia PDF encountered an uncollected glyph.");
        }

        if (glyphs.Length == 0)
        {
            return;
        }

        if (clusters.Length != glyphs.Length || clusters.Any(cluster => cluster >= source.Length))
        {
            throw new InvalidDataException("Skia PDF glyph clusters do not match source Unicode.");
        }

        if (!entry.Sizes.TryGetValue(size, out var font))
        {
            font = SkiaFontFace.CreateFont(entry.Subset ?? entry.Face.Typeface, size);
            if (entry.Color)
            {
                font.LinearMetrics = false;
                font.Subpixel = false;
                font.Hinting = SKFontHinting.Normal;
            }

            entry.Sizes.Add(size, font);
            ConversionMetrics.Report("pdfFontCreated", 1);
        }

        using var builder = new SKTextBlobBuilder();
        var buffer = builder.AllocatePositionedTextRun(font, glyphs.Length, source.Length);
        buffer.SetGlyphs(glyphs);
        buffer.SetPositions(entry.Face.Positions(glyphs, size));
        buffer.SetText(source);
        buffer.SetClusters(clusters);
        using var blob = builder.Build();
        canvas.DrawText(blob, x, y, paint);
    }

    private sealed class Entry(SkiaFontFace face)
    {
        internal SkiaFontFace Face { get; } = face;

        internal bool Color => Face.Color;

        internal HashSet<ushort> Glyphs { get; } = [0];

        internal Dictionary<float, SKFont> Sizes { get; } = new();

        internal SKTypeface? Subset { get; set; }
    }
}
