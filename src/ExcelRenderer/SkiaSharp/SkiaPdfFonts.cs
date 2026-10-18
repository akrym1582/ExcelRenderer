using System.Security.Cryptography;
using System.Text;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Collects glyphs and owns document-scoped subset faces, independently of measurement faces.</summary>
internal sealed class SkiaPdfFonts : IDisposable
{
    private readonly Dictionary<ResolvedFont, Entry> _fonts = new();
    private readonly Dictionary<string, Entry> _identities = new(StringComparer.Ordinal);
    private readonly CancellationToken _token;
    private readonly bool _fullEmbed;
    private bool _prepared;

    /// <summary>Initializes a new instance of the <see cref="SkiaPdfFonts"/> class.</summary>
    /// <param name="token">The conversion token.</param>
    /// <param name="fullEmbed">Internal comparison-only full embedding control.</param>
    internal SkiaPdfFonts(CancellationToken token, bool fullEmbed = false)
    {
        _token = token;
        _fullEmbed = fullEmbed;
    }

    /// <summary>Gets a value indicating whether only glyph usage is being collected.</summary>
    internal bool IsCollecting => !_prepared;

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var entry in _identities.Values)
        {
            entry.DisposeFonts();
            entry.Subset?.Dispose();
            entry.Original.Dispose();
        }

        _fonts.Clear();
        _identities.Clear();
        if (ConversionFontResources.Current is { } resources)
        {
            resources.RequestCachePurge();
        }
        else
        {
            SKGraphics.PurgeFontCache();
        }
    }

    /// <summary>Checks cancellation during recursive drawing command traversal.</summary>
    internal void CheckCancellation() => _token.ThrowIfCancellationRequested();

    /// <summary>Prepares one shared subset for each physical face after the collection pass.</summary>
    internal void Prepare()
    {
        foreach (var entry in _identities.Values)
        {
            _token.ThrowIfCancellationRequested();
            if (entry.Color || _fullEmbed)
            {
                continue;
            }

            var data = ConversionMetrics.Measure("pdfSubset", () => HarfBuzzSubset.Create(entry.Data, entry.Glyphs, _token));
            ConversionMetrics.Report("pdfOriginalFontBytes", entry.Data.Length);
            ConversionMetrics.Report("pdfSubsetFontBytes", data.Length);
            using var subsetData = SKData.CreateCopy(data);
            entry.Subset = SKTypeface.FromData(subsetData)
                ?? throw new InvalidDataException("Skia could not load the subset font.");
        }

        _prepared = true;
    }

    /// <summary>Collects or draws a resolved run with its unmodified source text and UTF-8 clusters.</summary>
    /// <param name="canvas">The page canvas.</param>
    /// <param name="run">The resolved run.</param>
    /// <param name="size">The finalized size.</param>
    /// <param name="x">The finalized horizontal position.</param>
    /// <param name="y">The finalized baseline.</param>
    /// <param name="paint">The run color.</param>
    internal void Draw(SKCanvas canvas, TextRun run, float size, float x, float y, SKPaint paint)
    {
        _token.ThrowIfCancellationRequested();
        var entry = GetEntry(run.Font);
        var original = entry.GetFont(size, subset: false);
        var glyphs = (run.GlyphId ?? run.ColorEmojiGlyphId) is { } glyph ? new[] { glyph } : original.GetGlyphs(run.Text);
        if (!_prepared)
        {
            foreach (var id in glyphs)
            {
                entry.Glyphs.Add(id);
            }

            return;
        }

        if (glyphs.Any(id => !entry.Glyphs.Contains(id)))
        {
            throw new InvalidOperationException("Skia PDF encountered a glyph that was absent from the collection pass.");
        }

        if (glyphs.Length == 0)
        {
            return;
        }

        var source = Encoding.UTF8.GetBytes(run.SourceText);
        var clusters = CreateClusters(run, glyphs.Length);

        // Native glyph metrics can quantize advances even with linear metrics enabled.
        // Use the original sfnt hmtx advances, as PDFsharp measurement does, and never
        // obtain positions from the subset face or reshape the finalized run.
        var positions = entry.GetPositions(glyphs, size);
        var font = entry.GetFont(size, subset: true);
        using var builder = new SKTextBlobBuilder();
        var buffer = builder.AllocatePositionedTextRun(font, glyphs.Length, source.Length);
        buffer.SetGlyphs(glyphs);
        buffer.SetPositions(positions);
        buffer.SetText(source);
        buffer.SetClusters(clusters);
        using var blob = builder.Build();
        canvas.DrawText(blob, x, y, paint);
    }

    private static uint[] CreateClusters(TextRun run, int glyphCount)
    {
        if (run.GlyphId is not null || run.ColorEmojiGlyphId is not null)
        {
            return new uint[glyphCount];
        }

        var clusters = new List<uint>();
        var rendered = System.Globalization.StringInfo.GetTextElementEnumerator(run.Text);
        var source = System.Globalization.StringInfo.GetTextElementEnumerator(run.SourceText);
        var byteOffset = 0;
        uint? whitespaceCluster = null;
        while (rendered.MoveNext())
        {
            if (!source.MoveNext())
            {
                throw new InvalidOperationException("Skia PDF source and display clusters differ.");
            }

            var displayElement = rendered.GetTextElement();
            var sourceElement = source.GetTextElement();
            var whitespace = displayElement.All(char.IsWhiteSpace);
            var cluster = whitespace ? whitespaceCluster ?? (uint)byteOffset : (uint)byteOffset;
            whitespaceCluster = whitespace ? cluster : null;
            for (var index = 0; index < displayElement.Length; index++)
            {
                clusters.Add(cluster);
                if (char.IsHighSurrogate(displayElement[index]) && index + 1 < displayElement.Length && char.IsLowSurrogate(displayElement[index + 1]))
                {
                    index++;
                }
            }

            byteOffset += Encoding.UTF8.GetByteCount(sourceElement);
        }

        if (source.MoveNext() || clusters.Count != glyphCount)
        {
            throw new InvalidOperationException("Skia PDF cannot map this run to its original Unicode clusters.");
        }

        return clusters.ToArray();
    }

    private Entry GetEntry(ResolvedFont font)
    {
        if (_fonts.TryGetValue(font, out var entry))
        {
            return entry;
        }

        var bytes = font.FontData ?? File.ReadAllBytes(font.FilePath);
        using var sha = SHA256.Create();
        var identity = Convert.ToBase64String(sha.ComputeHash(bytes));
        if (!_identities.TryGetValue(identity, out entry))
        {
            if (_prepared)
            {
                throw new InvalidOperationException("Skia PDF encountered a font that was absent from the collection pass.");
            }

            using var data = SKData.CreateCopy(bytes);
            var face = SKTypeface.FromData(data) ?? throw new InvalidDataException("Skia could not load the original font.");
            var tags = face.GetTableTags();
            entry = new(bytes, face, tags.Contains(0x43424454u) || tags.Contains(0x434f4c52u) || tags.Contains(0x73626978u));
            _identities.Add(identity, entry);
        }

        _fonts.Add(font, entry);
        return entry;
    }

    private sealed class Entry(byte[] data, SKTypeface original, bool color)
    {
        private readonly Dictionary<(float Size, bool Subset), SKFont> _sizes = new();
        private readonly byte[] _head = original.GetTableData(0x68656164);
        private readonly byte[] _hhea = original.GetTableData(0x68686561);
        private readonly byte[] _hmtx = original.GetTableData(0x686d7478);

        internal byte[] Data { get; } = data;

        internal SKTypeface Original { get; } = original;

        internal bool Color { get; } = color;

        internal HashSet<ushort> Glyphs { get; } = [0];

        internal SKTypeface? Subset { get; set; }

        internal void DisposeFonts()
        {
            foreach (var font in _sizes.Values)
            {
                font.Dispose();
            }

            _sizes.Clear();
        }

        internal SKFont GetFont(float size, bool subset)
        {
            var key = (size, subset);
            if (!_sizes.TryGetValue(key, out var font))
            {
                font = new SKFont(subset ? Subset ?? Original : Original, size)
                {
                    LinearMetrics = !Color,
                    Subpixel = !Color,
                    Hinting = Color ? SKFontHinting.Normal : SKFontHinting.None,
                };
                _sizes.Add(key, font);
            }

            return font;
        }

        internal SKPoint[] GetPositions(ushort[] glyphs, float size)
        {
            var head = _head;
            var hhea = _hhea;
            var hmtx = _hmtx;
            if (head.Length < 20 || hhea.Length < 36)
            {
                throw new InvalidDataException("Skia PDF requires valid sfnt head/hhea/hmtx metrics.");
            }

            var units = (head[18] << 8) | head[19];
            var count = (hhea[34] << 8) | hhea[35];
            if (units == 0 || count == 0 || hmtx.Length < count * 4)
            {
                throw new InvalidDataException("Skia PDF font advance metrics are truncated.");
            }

            var result = new SKPoint[glyphs.Length];
            double x = 0;
            for (var index = 0; index < glyphs.Length; index++)
            {
                result[index] = new((float)x, 0);
                var offset = Math.Min(glyphs[index], count - 1) * 4;
                var advance = (hmtx[offset] << 8) | hmtx[offset + 1];
                x += (double)size * advance / units;
            }

            return result;
        }
    }
}
