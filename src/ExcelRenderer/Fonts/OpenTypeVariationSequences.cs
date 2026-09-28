namespace ExcelRenderer.Fonts;

/// <summary>Reads the OpenType cmap format 14 variation-selector mapping.</summary>
internal static class OpenTypeVariationSequences
{
    /// <summary>Tests whether a font's format 14 cmap explicitly supports a base-scalar/selector pair.</summary>
    /// <param name="font">The complete OpenType font data.</param>
    /// <param name="baseScalar">The Unicode base scalar.</param>
    /// <param name="selector">The Unicode variation selector.</param>
    /// <returns><see langword="true"/> when format 14 contains the requested pair.</returns>
    internal static bool Supports(byte[] font, int baseScalar, int selector)
    {
        try
        {
            var cmap = FindTable(font, 0x636D6170); // cmap
            if (cmap < 0)
            {
                return false;
            }

            var count = U16(font, cmap + 2);
            for (var i = 0; i < count; i++)
            {
                var subtable = cmap + checked((int)U32(font, cmap + 4 + (i * 8) + 4));
                if (U16(font, subtable) == 14 && SupportsFormat14(font, subtable, baseScalar, selector))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or OverflowException)
        {
            return false;
        }

        return false;
    }

    private static bool SupportsFormat14(byte[] data, int offset, int scalar, int selector)
    {
        var records = checked((int)U32(data, offset + 6));
        for (var i = 0; i < records; i++)
        {
            var record = offset + 10 + (i * 11);
            if (U24(data, record) != selector)
            {
                continue;
            }

            var defaultOffset = checked((int)U32(data, record + 3));
            if (defaultOffset != 0)
            {
                var table = offset + defaultOffset;
                var ranges = checked((int)U32(data, table));
                for (var r = 0; r < ranges; r++)
                {
                    var start = U24(data, table + 4 + (r * 4));
                    var additional = data[table + 7 + (r * 4)];
                    if (scalar >= start && scalar <= start + additional)
                    {
                        return true;
                    }
                }
            }

            var nonDefaultOffset = checked((int)U32(data, record + 7));
            if (nonDefaultOffset != 0)
            {
                var table = offset + nonDefaultOffset;
                var mappings = checked((int)U32(data, table));
                for (var m = 0; m < mappings; m++)
                {
                    if (U24(data, table + 4 + (m * 5)) == scalar && U16(data, table + 7 + (m * 5)) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return false;
    }

    private static int FindTable(byte[] data, uint tag)
    {
        var tables = U16(data, 4);
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

    private static ushort U16(byte[] data, int offset) => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static int U24(byte[] data, int offset) => (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];

    private static uint U32(byte[] data, int offset) => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
