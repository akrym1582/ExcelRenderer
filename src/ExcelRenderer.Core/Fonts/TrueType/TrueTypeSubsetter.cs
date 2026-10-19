namespace ExcelRenderer.Core.Fonts.TrueType;

/// <summary>Builds PDF-only static TrueType subsets while preserving the original GID space.</summary>
internal static class TrueTypeSubsetter
{
    private static readonly HashSet<string> RetainedTables = new(StringComparer.Ordinal)
    {
        "head", "maxp", "hhea", "hmtx", "vhea", "vmtx", "cmap", "name", "OS/2", "post", "cvt ", "fpgm", "prep", "gasp",
    };

    /// <summary>Removes unused outlines without modifying the input, metrics, mapping or retained glyph bytes.</summary>
    /// <param name="fontData">The selected standalone face bytes.</param>
    /// <param name="usedGlyphIds">The finalized GIDs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Independent deterministic subset bytes.</returns>
    internal static byte[] Create(byte[] fontData, IEnumerable<ushort> usedGlyphIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var face = new SfntReader(fontData, token: cancellationToken);
        var offsets = TrueTypeFontValidator.Validate(face, cancellationToken);
        var original = face.Get("glyf");
        var keep = TrueTypeGlyphClosure.Collect(original, offsets, usedGlyphIds, cancellationToken);
        var length = 0;
        for (var glyph = 0; glyph < keep.Length; glyph++)
        {
            if (keep[glyph])
            {
                length = checked(length + ((offsets[glyph + 1] - offsets[glyph] + 1) & ~1));
            }
        }

        var glyf = new byte[length];
        var loca = new byte[offsets.Length * 4];
        var head = face.Get("head").ToArray();
        var position = 0;
        var hasBounds = false;
        var bounds = new int[4];
        for (var glyph = 0; glyph < keep.Length; glyph++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SfntWriter.Put32(loca, glyph * 4, (uint)position);
            if (!keep[glyph] || offsets[glyph] == offsets[glyph + 1])
            {
                continue;
            }

            var data = original.Slice(offsets[glyph], offsets[glyph + 1] - offsets[glyph]);
            data.CopyTo(glyf.AsSpan(position));
            position += (data.Length + 1) & ~1;
            if (SfntReader.I16(data, 0) != 0)
            {
                for (var coordinate = 0; coordinate < 4; coordinate++)
                {
                    var value = SfntReader.I16(data, 2 + (coordinate * 2));
                    bounds[coordinate] = !hasBounds ? value : coordinate < 2 ? Math.Min(bounds[coordinate], value) : Math.Max(bounds[coordinate], value);
                }

                hasBounds = true;
            }
        }

        SfntWriter.Put32(loca, keep.Length * 4, (uint)position);
        SfntWriter.Put16(head, 50, 1);
        for (var coordinate = 0; coordinate < 4; coordinate++)
        {
            SfntWriter.Put16(head, 36 + (coordinate * 2), unchecked((ushort)bounds[coordinate]));
        }

        var tables = face.Tables.Where(entry => RetainedTables.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => new ReadOnlyMemory<byte>(fontData, entry.Value.Offset, entry.Value.Length), StringComparer.Ordinal);
        tables["head"] = head;
        tables["glyf"] = glyf;
        tables["loca"] = loca;
        return SfntWriter.Write(face.Version, tables, cancellationToken);
    }
}
