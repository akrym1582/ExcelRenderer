using System.Text;
using ExcelRenderer.Core.Layout;

namespace ExcelRenderer.Core.Fonts;

/// <summary>Stores immutable glyph selection and source clusters without retaining native handles.</summary>
internal sealed class GlyphSequence
{
    private readonly ushort[] glyphs;
    private readonly byte[] source;
    private readonly uint[] clusters;

    /// <summary>Initializes a new instance of the <see cref="GlyphSequence"/> class.</summary>
    /// <param name="face">The selected physical snapshot.</param>
    /// <param name="glyphs">The selected IDs.</param>
    /// <param name="display">The display text.</param>
    /// <param name="source">The original source text.</param>
    /// <param name="singleCluster">Whether one explicit glyph represents the source sequence.</param>
    internal GlyphSequence(SkiaFontFace face, ushort[] glyphs, string display, string source, bool singleCluster = false)
        : this(face, glyphs.AsSpan(0, glyphs.Length), display, source, singleCluster)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GlyphSequence"/> class from borrowed glyphs.</summary>
    /// <param name="face">The selected physical snapshot.</param>
    /// <param name="glyphs">The borrowed range; copied exactly once into privately owned storage.</param>
    /// <param name="display">The display text.</param>
    /// <param name="source">The original source text.</param>
    /// <param name="singleCluster">Whether one explicit glyph represents the source sequence.</param>
    internal GlyphSequence(SkiaFontFace face, ReadOnlySpan<ushort> glyphs, string display, string source, bool singleCluster = false)
    {
        FontIdentity = face.Identity;
        this.glyphs = glyphs.ToArray();
        this.source = Encoding.UTF8.GetBytes(source);
        Glyphs = Array.AsReadOnly(this.glyphs);
        Source = Array.AsReadOnly(this.source);
        this.clusters = singleCluster ? new uint[glyphs.Length] :
            string.Equals(display, source, StringComparison.Ordinal) ? IdentityClusters(glyphs.Length, source) :
            CompatibilityClusters(glyphs.Length, display, source);
        Clusters = Array.AsReadOnly(this.clusters);
        Advances = Array.AsReadOnly(face.Advances(this.glyphs, 1));
        Rendering.ConversionMetrics.Report("glyphStorageCopies", 1);
        Rendering.ConversionMetrics.Report("glyphStorageCopiedGlyphs", glyphs.Length);
    }

    /// <summary>Gets the immutable physical content identity.</summary>
    internal string FontIdentity { get; }

    /// <summary>Gets the selected glyph IDs.</summary>
    internal IReadOnlyList<ushort> Glyphs { get; }

    /// <summary>Gets original UTF-8 bytes.</summary>
    internal IReadOnlyList<byte> Source { get; }

    /// <summary>Gets original UTF-8 offsets per glyph.</summary>
    internal IReadOnlyList<uint> Clusters { get; }

    /// <summary>Gets per-glyph advances at one point; offsets are zero under the existing nonshaping policy.</summary>
    internal IReadOnlyList<double> Advances { get; }

    /// <summary>Gets borrowed immutable glyphs for native APIs that copy their input.</summary>
    internal ReadOnlySpan<ushort> GlyphSpan => glyphs;

    /// <summary>Gets borrowed immutable UTF-8 text for native APIs that copy their input.</summary>
    internal ReadOnlySpan<byte> SourceSpan => source;

    /// <summary>Gets borrowed immutable clusters for native APIs that copy their input.</summary>
    internal ReadOnlySpan<uint> ClusterSpan => clusters;

    private static uint[] IdentityClusters(int glyphCount, string text)
    {
        var clusters = new uint[glyphCount];
        var glyphIndex = 0;
        var offset = 0;
        uint? whitespace = null;
        foreach (var element in TextLineBreaker.Elements(text))
        {
            var isWhitespace = true;
            for (var index = 0; index < element.Length; index++)
            {
                if (!char.IsWhiteSpace(element[index]))
                {
                    isWhitespace = false;
                    break;
                }
            }

            var cluster = isWhitespace ? whitespace ?? (uint)offset : (uint)offset;
            whitespace = isWhitespace ? cluster : null;
            for (var index = 0; index < element.Length; index++)
            {
                if (glyphIndex == glyphCount)
                {
                    throw new InvalidDataException("Glyphs and source Unicode clusters differ.");
                }

                clusters[glyphIndex++] = cluster;
                if (char.IsHighSurrogate(element[index]) && index + 1 < element.Length && char.IsLowSurrogate(element[index + 1]))
                {
                    index++;
                }
            }

            offset += Encoding.UTF8.GetByteCount(element);
        }

        if (glyphIndex != glyphCount)
        {
            throw new InvalidDataException("Glyphs and source Unicode clusters differ.");
        }

        Rendering.ConversionMetrics.Report("glyphClusterElementPasses", 1);
        return clusters;
    }

    private static uint[] CompatibilityClusters(int glyphCount, string display, string source)
    {
        var clusters = new List<uint>();
        using var originals = TextLineBreaker.Elements(source).GetEnumerator();
        var offset = 0;
        uint? whitespace = null;
        foreach (var element in TextLineBreaker.Elements(display))
        {
            if (!originals.MoveNext())
            {
                throw new InvalidDataException("Display and source Unicode clusters differ.");
            }

            var isWhitespace = element.All(char.IsWhiteSpace);
            var cluster = isWhitespace ? whitespace ?? (uint)offset : (uint)offset;
            whitespace = isWhitespace ? cluster : null;
            for (var index = 0; index < element.Length; index++)
            {
                clusters.Add(cluster);
                if (char.IsHighSurrogate(element[index]) && index + 1 < element.Length && char.IsLowSurrogate(element[index + 1]))
                {
                    index++;
                }
            }

            offset += Encoding.UTF8.GetByteCount(originals.Current);
        }

        if (originals.MoveNext())
        {
            throw new InvalidDataException("Unused source Unicode clusters.");
        }

        if (clusters.Count != glyphCount)
        {
            throw new InvalidDataException("Glyphs and source Unicode clusters differ.");
        }

        Rendering.ConversionMetrics.Report("glyphClusterElementPasses", 2);
        return clusters.ToArray();
    }
}
