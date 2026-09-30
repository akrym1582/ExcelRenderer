namespace ExcelRenderer.Fonts;

/// <summary>Reads Unicode variation sequences and ordinary Unicode mappings from an OpenType cmap.</summary>
internal static class OpenTypeVariationSequences
{
    /// <summary>Resolves a base scalar and variation selector to the glyph selected by cmap format 14.</summary>
    /// <param name="font">The OpenType font data.</param>
    /// <param name="baseScalar">The base Unicode scalar.</param>
    /// <param name="selector">The variation selector.</param>
    /// <param name="resolution">The resolved glyph information when the method succeeds.</param>
    /// <returns><see langword="true"/> when the variation sequence resolves to a glyph; otherwise, <see langword="false"/>.</returns>
    internal static bool TryResolve(byte[] font, int baseScalar, int selector, out Resolution resolution)
    {
        resolution = default;
        try
        {
            var cmap = FindTable(font, 0x636D6170);
            if (cmap < 0)
            {
                return false;
            }

            var count = U16(font, cmap + 2);
            for (var i = 0; i < count; i++)
            {
                var subtable = cmap + checked((int)U32(font, cmap + 8 + (i * 8)));
                if (U16(font, subtable) != 14)
                {
                    continue;
                }

                if (TryResolveFormat14(font, cmap, count, subtable, baseScalar, selector, out resolution))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or OverflowException)
        {
            resolution = default;
        }

        return false;
    }

    private static bool TryResolveFormat14(byte[] data, int cmap, int cmapCount, int offset, int scalar, int selector, out Resolution resolution)
    {
        resolution = default;
        var length = checked((int)U32(data, offset + 2));
        CheckRange(data, offset, length);
        var records = checked((int)U32(data, offset + 6));
        CheckRange(data, offset + 10, checked(records * 11));
        for (var i = 0; i < records; i++)
        {
            var record = offset + 10 + (i * 11);
            if (U24(data, record) != selector)
            {
                continue;
            }

            var nonDefaultOffset = checked((int)U32(data, record + 7));
            if (nonDefaultOffset != 0)
            {
                var table = checked(offset + nonDefaultOffset);
                var mappings = checked((int)U32(data, table));
                CheckRange(data, table + 4, checked(mappings * 5));
                for (var m = 0; m < mappings; m++)
                {
                    var mapping = table + 4 + (m * 5);
                    if (U24(data, mapping) == scalar)
                    {
                        var glyph = U16(data, mapping + 3);
                        if (glyph != 0)
                        {
                            resolution = new(glyph, false);
                        }

                        return glyph != 0;
                    }
                }
            }

            var defaultOffset = checked((int)U32(data, record + 3));
            if (defaultOffset == 0 || !InDefaultRange(data, offset + defaultOffset, scalar))
            {
                return false;
            }

            var baseGlyph = ResolveBaseGlyph(data, cmap, cmapCount, scalar);
            if (baseGlyph == 0)
            {
                return false;
            }

            resolution = new(baseGlyph, true);
            return true;
        }

        return false;
    }

    private static bool InDefaultRange(byte[] data, int table, int scalar)
    {
        var ranges = checked((int)U32(data, table));
        CheckRange(data, table + 4, checked(ranges * 4));
        for (var i = 0; i < ranges; i++)
        {
            var start = U24(data, table + 4 + (i * 4));
            if (scalar >= start && scalar <= start + data[table + 7 + (i * 4)])
            {
                return true;
            }
        }

        return false;
    }

    private static ushort ResolveBaseGlyph(byte[] data, int cmap, int count, int scalar)
    {
        for (var i = 0; i < count; i++)
        {
            var table = cmap + checked((int)U32(data, cmap + 8 + (i * 8)));
            var format = U16(data, table);
            if (format == 12)
            {
                var groups = checked((int)U32(data, table + 12));
                CheckRange(data, table + 16, checked(groups * 12));
                for (var g = 0; g < groups; g++)
                {
                    var p = table + 16 + (g * 12);
                    var start = U32(data, p);
                    var end = U32(data, p + 4);
                    if ((uint)scalar >= start && (uint)scalar <= end)
                    {
                        return checked((ushort)(U32(data, p + 8) + (uint)scalar - start));
                    }
                }
            }
            else if (format == 4 && scalar <= ushort.MaxValue)
            {
                var segCount = U16(data, table + 6) / 2;
                var endCodes = table + 14;
                var startCodes = endCodes + (segCount * 2) + 2;
                var deltas = startCodes + (segCount * 2);
                var rangeOffsets = deltas + (segCount * 2);
                CheckRange(data, endCodes, checked((segCount * 8) + 2));
                for (var s = 0; s < segCount; s++)
                {
                    if (scalar < U16(data, startCodes + (s * 2)) || scalar > U16(data, endCodes + (s * 2)))
                    {
                        continue;
                    }

                    var range = U16(data, rangeOffsets + (s * 2));
                    if (range == 0)
                    {
                        return (ushort)((scalar + (short)U16(data, deltas + (s * 2))) & 0xffff);
                    }

                    var glyphAddress = rangeOffsets + (s * 2) + range + ((scalar - U16(data, startCodes + (s * 2))) * 2);
                    var glyph = U16(data, glyphAddress);
                    return glyph == 0 ? (ushort)0 : (ushort)((glyph + (short)U16(data, deltas + (s * 2))) & 0xffff);
                }
            }
        }

        return 0;
    }

    private static int FindTable(byte[] data, uint tag)
    {
        var tables = U16(data, 4);
        CheckRange(data, 12, checked(tables * 16));
        for (var i = 0; i < tables; i++)
        {
            var record = 12 + (i * 16);
            if (U32(data, record) == tag)
            {
                return checked((int)U32(data, record + 8));
            }
        }

        return -1;
    }

    private static void CheckRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new IndexOutOfRangeException();
        }
    }

    private static ushort U16(byte[] data, int offset)
    {
        CheckRange(data, offset, 2);
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static int U24(byte[] data, int offset)
    {
        CheckRange(data, offset, 3);
        return (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
    }

    private static uint U32(byte[] data, int offset)
    {
        CheckRange(data, offset, 4);
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    /// <summary>Represents the result of resolving a Unicode variation sequence.</summary>
    /// <param name="GlyphId">The resolved glyph identifier.</param>
    /// <param name="IsDefault">Whether the selector uses the default glyph mapping.</param>
    internal readonly record struct Resolution(ushort GlyphId, bool IsDefault);
}
