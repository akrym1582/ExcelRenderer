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
    {
        FontIdentity = face.Identity;
        this.glyphs = (ushort[])glyphs.Clone();
        this.source = Encoding.UTF8.GetBytes(source);
        Glyphs = Array.AsReadOnly(this.glyphs);
        Source = Array.AsReadOnly(this.source);
        var clusters = new List<uint>();
        if (singleCluster)
        {
            clusters.AddRange(new uint[glyphs.Length]);
        }
        else
        {
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
        }

        if (clusters.Count != glyphs.Length)
        {
            throw new InvalidDataException("Glyphs and source Unicode clusters differ.");
        }

        this.clusters = clusters.ToArray();
        Clusters = Array.AsReadOnly(this.clusters);
        Advances = Array.AsReadOnly(face.Advances(glyphs, 1));
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
}
