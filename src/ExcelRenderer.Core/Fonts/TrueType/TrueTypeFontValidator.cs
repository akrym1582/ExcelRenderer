namespace ExcelRenderer.Core.Fonts.TrueType;

/// <summary>Validates the structures consumed by the PDF subset and native font reader.</summary>
internal static class TrueTypeFontValidator
{
    /// <summary>Validates a static noncolor outline font and reads all loca offsets.</summary>
    /// <param name="face">The bounded face.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The N+1 glyph offsets.</returns>
    internal static int[] Validate(SfntReader face, CancellationToken token)
    {
        if (FontCapabilities.ValidatePdf(face, token))
        {
            throw new NotSupportedException("Color fonts use the original Skia face and cannot enter the outline subsetter.");
        }

        var head = face.Get("head", 54);
        var maxp = face.Get("maxp", 32);
        if (SfntReader.U32(maxp, 0) != 0x00010000)
        {
            throw SfntReader.Invalid("maxp", "TrueType version 1.0 required");
        }

        var glyphCount = SfntReader.U16(maxp, 4);
        var format = SfntReader.I16(head, 50);
        if (format is not (0 or 1))
        {
            throw SfntReader.Invalid("head", "indexToLocFormat must be 0 or 1");
        }

        var loca = face.Get("loca", (glyphCount + 1) * (format == 0 ? 2 : 4));
        var glyf = face.Get("glyf");
        var offsets = new int[glyphCount + 1];
        for (var i = 0; i < offsets.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var offset = format == 0 ? SfntReader.U16(loca, i * 2) * 2 : SfntReader.Offset(loca, i * 4, "loca");
            if (offset > glyf.Length || (i > 0 && offset < offsets[i - 1]))
            {
                throw SfntReader.Invalid("loca", "decreasing or out-of-range glyph offset");
            }

            offsets[i] = offset;
        }

        if (face.Tables.ContainsKey("cvt ") && (face.Get("cvt ").Length & 1) != 0)
        {
            throw SfntReader.Invalid("cvt ", "odd word table length");
        }

        if (face.Tables.ContainsKey("gasp"))
        {
            var gasp = face.Get("gasp", 4);
            SfntReader.Range(gasp.Length, 4, SfntReader.U16(gasp, 2) * 4, "gasp");
        }

        return offsets;
    }

    /// <summary>Validates common metrics and retained character/name tables for either PDF route.</summary>
    /// <param name="face">The bounded face.</param>
    /// <param name="token">The cancellation token.</param>
    internal static void ValidateMetrics(SfntReader face, CancellationToken token = default)
    {
        var head = face.Get("head", 54);
        if (SfntReader.U32(head, 0) != 0x00010000 || SfntReader.U32(head, 12) != 0x5f0f3cf5 ||
            SfntReader.U16(head, 18) is < 16 or > 16384 || SfntReader.I16(head, 52) != 0)
        {
            throw SfntReader.Invalid("head", "invalid version, magic, unitsPerEm or glyphDataFormat");
        }

        var glyphCount = SfntReader.U16(face.Get("maxp", 6), 4);
        if (glyphCount == 0)
        {
            throw SfntReader.Invalid("maxp", "numGlyphs must be positive");
        }

        ValidateMetricPair(face, "hhea", "hmtx", glyphCount);
        if (face.Tables.ContainsKey("vhea") || face.Tables.ContainsKey("vmtx"))
        {
            ValidateMetricPair(face, "vhea", "vmtx", glyphCount);
        }

        ValidateCmap(face.Get("cmap", 4), glyphCount, token);
        ValidateName(face.Get("name", 6), token);
        var os2 = face.Get("OS/2", 78);
        var version = SfntReader.U16(os2, 0);
        var minimum = version switch { 0 => 78, 1 => 86, 2 or 3 or 4 => 96, 5 => 100, _ => -1 };
        if (minimum < 0 || os2.Length < minimum)
        {
            throw SfntReader.Invalid("OS/2", "invalid version or table length");
        }

        var post = face.Get("post", 32);
        var postVersion = SfntReader.U32(post, 0);
        if (postVersion == 0x00020000)
        {
            if (SfntReader.U16(post, 32) != glyphCount)
            {
                throw SfntReader.Invalid("post", "glyph count mismatch");
            }

            SfntReader.Range(post.Length, 34, glyphCount * 2, "post");
            var names = 0;
            for (var i = 0; i < glyphCount; i++)
            {
                token.ThrowIfCancellationRequested();
                names = Math.Max(names, SfntReader.U16(post, 34 + (i * 2)) - 257);
            }

            var position = 34 + (glyphCount * 2);
            for (var i = 0; i < names; i++)
            {
                SfntReader.Range(post.Length, position, 1, "post");
                var length = post[position++];
                SfntReader.Range(post.Length, position, length, "post");
                position += length;
            }
        }
        else if (postVersion is not (0x00010000 or 0x00030000))
        {
            throw new NotSupportedException($"Unsupported post table version 0x{postVersion:X8}.");
        }
    }

    private static void ValidateMetricPair(SfntReader face, string headerTag, string metricsTag, int glyphCount)
    {
        var header = face.Get(headerTag, 36);
        var version = SfntReader.U32(header, 0);
        var count = SfntReader.U16(header, 34);
        if ((version != 0x00010000 && !(headerTag == "vhea" && version == 0x00011000)) || count == 0 || count > glyphCount)
        {
            throw SfntReader.Invalid(headerTag, "invalid version or long metric count");
        }

        face.Get(metricsTag, (count * 4) + ((glyphCount - count) * 2));
    }

    private static void ValidateName(ReadOnlySpan<byte> name, CancellationToken token)
    {
        var format = SfntReader.U16(name, 0);
        var count = SfntReader.U16(name, 2);
        var storage = SfntReader.U16(name, 4);
        var recordsEnd = 6 + (count * 12);
        SfntReader.Range(name.Length, 0, recordsEnd, "name");
        if (format > 1 || storage < recordsEnd || storage > name.Length)
        {
            throw SfntReader.Invalid("name", "invalid format or string storage");
        }

        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var position = 6 + (i * 12);
            SfntReader.Range(name.Length, storage + SfntReader.U16(name, position + 10), SfntReader.U16(name, position + 8), "name");
        }

        if (format == 1)
        {
            var languages = SfntReader.U16(name, recordsEnd);
            SfntReader.Range(name.Length, recordsEnd + 2, languages * 4, "name");
            if (storage < recordsEnd + 2 + (languages * 4))
            {
                throw SfntReader.Invalid("name", "language records overlap string storage");
            }

            for (var i = 0; i < languages; i++)
            {
                var position = recordsEnd + 2 + (i * 4);
                SfntReader.Range(name.Length, storage + SfntReader.U16(name, position + 2), SfntReader.U16(name, position), "name");
            }
        }
    }

    private static void ValidateCmap(ReadOnlySpan<byte> cmap, int glyphCount, CancellationToken token)
    {
        var count = SfntReader.U16(cmap, 2);
        SfntReader.Range(cmap.Length, 4, count * 8, "cmap");
        if (SfntReader.U16(cmap, 0) != 0 || count == 0)
        {
            throw SfntReader.Invalid("cmap", "invalid header");
        }

        var checkedOffsets = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var offset = SfntReader.Offset(cmap, 8 + (i * 8), "cmap");
            if (offset < 4 + (count * 8))
            {
                throw SfntReader.Invalid("cmap", "subtable overlaps directory");
            }

            if (!checkedOffsets.Add(offset))
            {
                continue;
            }

            var format = SfntReader.U16(cmap, offset);
            var length = format == 14 ? SfntReader.Offset(cmap, offset + 2, "cmap format 14")
                : format is 8 or 10 or 12 or 13 ? SfntReader.Offset(cmap, offset + 4, "cmap") : SfntReader.U16(cmap, offset + 2);
            SfntReader.Range(cmap.Length, offset, length, "cmap");
            var table = cmap.Slice(offset, length);
            switch (format)
            {
                case 0:
                    SfntReader.Range(length, 0, 262, "cmap format 0");
                    for (var glyph = 6; glyph < 262; glyph++)
                    {
                        if (table[glyph] >= glyphCount)
                        {
                            throw SfntReader.Invalid("cmap format 0", "glyph ID out of range");
                        }
                    }

                    break;
                case 4:
                    ValidateCmap4(table, glyphCount, token);
                    break;
                case 6:
                    var entries = SfntReader.U16(table, 8);
                    SfntReader.Range(length, 10, entries * 2, "cmap format 6");
                    if (SfntReader.U16(table, 6) + entries > 65536)
                    {
                        throw SfntReader.Invalid("cmap format 6", "character range exceeds BMP");
                    }

                    for (var glyph = 0; glyph < entries; glyph++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (SfntReader.U16(table, 10 + (glyph * 2)) >= glyphCount)
                        {
                            throw SfntReader.Invalid("cmap format 6", "glyph ID out of range");
                        }
                    }

                    break;
                case 12:
                case 13:
                    ValidateCmapGroups(table, glyphCount, format == 13, token);
                    break;
                case 14:
                    ValidateCmap14(table, glyphCount, token);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported cmap format {format} in PDF font.");
            }
        }
    }

    private static void ValidateCmap4(ReadOnlySpan<byte> table, int glyphCount, CancellationToken token)
    {
        var segmentBytes = SfntReader.U16(table, 6);
        var segments = segmentBytes / 2;
        if (segmentBytes == 0 || (segmentBytes & 1) != 0)
        {
            throw SfntReader.Invalid("cmap format 4", "invalid segment count");
        }

        SfntReader.Range(table.Length, 0, 16 + (segments * 8), "cmap format 4");
        var previous = -1;
        for (var i = 0; i < segments; i++)
        {
            var end = SfntReader.U16(table, 14 + (i * 2));
            var start = SfntReader.U16(table, 16 + (segments * 2) + (i * 2));
            var delta = SfntReader.I16(table, 16 + (segments * 4) + (i * 2));
            var rangePosition = 16 + (segments * 6) + (i * 2);
            var range = SfntReader.U16(table, rangePosition);
            if (start > end || start <= previous || (range & 1) != 0 ||
                (range != 0 && rangePosition + range < 16 + (segments * 8)))
            {
                throw SfntReader.Invalid("cmap format 4", "invalid segment order/range");
            }

            for (var code = (int)start; code <= end; code++)
            {
                token.ThrowIfCancellationRequested();
                var glyph = range == 0 ? unchecked((ushort)(code + delta)) : SfntReader.U16(table, rangePosition + range + ((code - start) * 2));
                if (range != 0 && glyph != 0)
                {
                    glyph = unchecked((ushort)(glyph + delta));
                }

                if (glyph >= glyphCount)
                {
                    throw SfntReader.Invalid("cmap format 4", "glyph ID out of range");
                }
            }

            previous = end;
        }
    }

    private static void ValidateCmapGroups(ReadOnlySpan<byte> table, int glyphCount, bool constant, CancellationToken token)
    {
        var count = SfntReader.Offset(table, 12, "cmap groups");
        if (count > (table.Length - 16) / 12)
        {
            throw SfntReader.Invalid("cmap groups", "truncated group records");
        }

        long previous = -1;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var position = 16 + (i * 12);
            var start = SfntReader.U32(table, position);
            var end = SfntReader.U32(table, position + 4);
            var glyph = SfntReader.U32(table, position + 8);
            if (start > end || start <= previous || end > 0x10ffff || (long)glyph + (constant ? 0 : end - start) >= glyphCount)
            {
                throw SfntReader.Invalid("cmap groups", "invalid Unicode range or glyph ID");
            }

            previous = end;
        }
    }

    private static void ValidateCmap14(ReadOnlySpan<byte> table, int glyphCount, CancellationToken token)
    {
        var count = SfntReader.Offset(table, 6, "cmap format 14");
        if (count > (table.Length - 10) / 11)
        {
            throw SfntReader.Invalid("cmap format 14", "truncated selector records");
        }

        var previousSelector = -1;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var position = 10 + (i * 11);
            var selector = Read24(table, position);
            if (selector <= previousSelector || selector is not (>= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef))
            {
                throw SfntReader.Invalid("cmap format 14", "invalid variation selector order/value");
            }

            previousSelector = selector;
            for (var kind = 0; kind < 2; kind++)
            {
                var offset = SfntReader.Offset(table, position + 3 + (kind * 4), "cmap format 14");
                if (offset == 0)
                {
                    continue;
                }

                var records = SfntReader.Offset(table, offset, "cmap format 14");
                var width = kind == 0 ? 4 : 5;
                if (offset < 10 + (count * 11) || records > (table.Length - offset - 4) / width)
                {
                    throw SfntReader.Invalid("cmap format 14", "invalid variation mapping range");
                }

                var previous = -1;
                for (var j = 0; j < records; j++)
                {
                    token.ThrowIfCancellationRequested();
                    var record = offset + 4 + (j * width);
                    var codepoint = Read24(table, record);
                    var end = codepoint + (kind == 0 ? table[record + 3] : 0);
                    if (codepoint <= previous || end > 0x10ffff || (kind == 1 && SfntReader.U16(table, record + 3) >= glyphCount))
                    {
                        throw SfntReader.Invalid("cmap format 14", "invalid variation Unicode range or glyph ID");
                    }

                    previous = end;
                }
            }
        }
    }

    private static int Read24(ReadOnlySpan<byte> data, int offset)
    {
        SfntReader.Range(data.Length, offset, 3, "cmap format 14");
        return (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
    }
}
