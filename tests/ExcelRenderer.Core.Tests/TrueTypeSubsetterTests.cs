using System.Buffers.Binary;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Fonts.TrueType;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class TrueTypeSubsetterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retains_ids_metrics_instructions_and_transitive_components(bool shortLoca)
    {
        var bytes = MakeFont(shortLoca);
        var unchanged = bytes.ToArray();
        var subset = TrueTypeSubsetter.Create(bytes, [7, 7, 1], default);
        Assert.Equal(unchanged, bytes);
        Assert.Equal(subset, TrueTypeSubsetter.Create(bytes, [1, 7], default));
        var before = new SfntReader(bytes);
        var after = new SfntReader(subset);
        var a = TrueTypeFontValidator.Validate(before, default);
        var b = TrueTypeFontValidator.Validate(after, default);
        Assert.Equal(a.Length, b.Length);
        Assert.Equal(1, SfntReader.U16(after.Get("head"), 50));
        Assert.Equal(before.Get("hmtx").ToArray(), after.Get("hmtx").ToArray());
        Assert.Equal(before.Get("hhea").ToArray(), after.Get("hhea").ToArray());
        Assert.Equal(before.Get("cmap").ToArray(), after.Get("cmap").ToArray());
        Assert.Equal(before.Get("maxp").ToArray(), after.Get("maxp").ToArray());
        // 7 -> 6, 5 -> 4 -> 3, 2. All shared descendants remain, unused 8 is empty.
        foreach (var glyph in new[] { 0, 2, 3, 4, 5, 6, 7 })
        {
            Assert.Equal(before.Get("glyf").Slice(a[glyph], a[glyph + 1] - a[glyph]).ToArray(),
                after.Get("glyf").Slice(b[glyph], b[glyph + 1] - b[glyph]).ToArray());
        }

        Assert.Equal(b[1], b[2]);
        Assert.Equal(b[8], b[9]);
        Assert.Equal(0xB1B0AFBAu, IndependentChecksum(subset));
        ValidateDirectoryChecksums(subset);
        Assert.DoesNotContain("GSUB", after.Tables.Keys);
        Assert.DoesNotContain("DSIG", after.Tables.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_and_complete_usage_keep_notdef_and_original_gid_space(bool shortLoca)
    {
        var bytes = MakeFont(shortLoca);
        var none = new SfntReader(TrueTypeSubsetter.Create(bytes, [], default));
        var offsets = TrueTypeFontValidator.Validate(none, default);
        Assert.True(offsets[1] > 0);
        Assert.All(offsets.Skip(1), offset => Assert.Equal(offsets[1], offset));
        var all = new SfntReader(TrueTypeSubsetter.Create(bytes, Enumerable.Range(0, 9).Select(value => (ushort)value), default));
        var original = new SfntReader(bytes);
        Assert.Equal(original.Get("glyf").ToArray(), all.Get("glyf").ToArray());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0x100)]
    [InlineData(0x200)]
    public void Rejects_embedding_restrictions_without_clearing_bits(int restrictions)
    {
        var bytes = MakeFont();
        var face = new SfntReader(bytes);
        Put16(bytes, face.Tables["OS/2"].Offset + 8, restrictions);
        var copy = bytes.ToArray();
        var error = Assert.Throws<NotSupportedException>(() => TrueTypeSubsetter.Create(bytes, [2], default));
        Assert.Contains("fsType", error.Message);
        Assert.Equal(copy, bytes);
    }

    [Theory]
    [InlineData("fvar")]
    [InlineData("gvar")]
    [InlineData("avar")]
    [InlineData("HVAR")]
    [InlineData("VVAR")]
    [InlineData("MVAR")]
    [InlineData("cvar")]
    [InlineData("CFF ")]
    [InlineData("CFF2")]
    public void Rejects_unsupported_tables_before_color_branch(string tag)
    {
        var tables = Tables(MakeFont());
        tables[tag] = new byte[16];
        tables["COLR"] = new byte[16];
        var bytes = Write(tables);
        Assert.Throws<NotSupportedException>(() => FontCapabilities.ValidatePdf(new SfntReader(bytes)));
    }

    [Theory]
    [InlineData("cff.otf")]
    [InlineData("variable.ttf")]
    public void Rejects_real_unsupported_formats(string name) =>
        Assert.Throws<NotSupportedException>(() => TrueTypeSubsetter.Create(Fixture(name), [2], default));

    [Theory]
    [InlineData(0x774f4646u, "WOFF")]
    [InlineData(0x774f4632u, "WOFF2")]
    [InlineData(0x74727565u, "true")]
    [InlineData(0x74797031u, "typ1")]
    public void Reports_unsupported_container_and_legacy_signatures(uint version, string expected)
    {
        var bytes = MakeFont();
        Put32(bytes, 0, version);
        var error = Assert.Throws<NotSupportedException>(() => TrueTypeSubsetter.Create(bytes, [2], default));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Stat_and_ivs_do_not_mark_a_face_variable()
    {
        var tables = Tables(Fixture("static.ttf"));
        tables["STAT"] = new byte[16];
        Assert.False(FontCapabilities.ValidatePdf(new SfntReader(Write(tables))));
    }

    [Fact]
    public void Cancellation_propagates_before_and_during_glyph_collection()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => TrueTypeSubsetter.Create(MakeFont(), [], source.Token));
        using var during = new CancellationTokenSource();
        IEnumerable<ushort> Glyphs()
        {
            yield return 2;
            during.Cancel();
            yield return 3;
        }

        Assert.Throws<OperationCanceledException>(() => TrueTypeSubsetter.Create(MakeFont(), Glyphs(), during.Token));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("offset")]
    [InlineData("length")]
    [InlineData("duplicate")]
    [InlineData("overlap")]
    [InlineData("directory")]
    [InlineData("loca-order")]
    [InlineData("loca-range")]
    [InlineData("loca-truncated-header")]
    [InlineData("bad-component")]
    [InlineData("cycle")]
    [InlineData("truncated-component")]
    [InlineData("truncated-simple")]
    [InlineData("truncated-instructions")]
    [InlineData("repeat-overflow")]
    [InlineData("metrics")]
    public void Rejects_corrupt_structures_with_managed_diagnostics(string corruption)
    {
        var bytes = MakeFont();
        var face = new SfntReader(bytes);
        var glyf = face.Tables["glyf"].Offset;
        var loca = face.Tables["loca"].Offset;
        var offsets = TrueTypeFontValidator.Validate(face, default);
        switch (corruption)
        {
            case "header": bytes = bytes[..5]; break;
            case "offset": Put32(bytes, 20, uint.MaxValue); break;
            case "length": Put32(bytes, 24, uint.MaxValue); break;
            case "duplicate": bytes.AsSpan(12, 4).CopyTo(bytes.AsSpan(28, 4)); break;
            case "overlap": Put32(bytes, 36, Read32(bytes, 20)); break;
            case "directory": Put32(bytes, 20, 12); break;
            case "loca-order": Put32(bytes, loca + 12, 0); break;
            case "loca-range": Put32(bytes, loca + 32, uint.MaxValue); break;
            case "loca-truncated-header": Put32(bytes, loca + 4, 1); break;
            case "bad-component": Put16(bytes, glyf + offsets[4] + 12, 500); break;
            case "cycle": Put16(bytes, glyf + offsets[4] + 12, 7); break;
            case "truncated-component": Put32(bytes, loca + 20, (uint)(offsets[4] + 12)); break;
            case "truncated-simple": Put16(bytes, glyf + offsets[2] + 10, 65535); break;
            case "truncated-instructions": Put16(bytes, glyf + offsets[6] + 20, 65535); break;
            case "repeat-overflow": bytes[glyf + offsets[2] + 14] = 0x39; bytes[glyf + offsets[2] + 15] = 255; break;
            case "metrics": Put16(bytes, face.Tables["hhea"].Offset + 34, 500); break;
        }

        Assert.Throws<InvalidDataException>(() => TrueTypeSubsetter.Create(bytes, [7], default));
    }

    [Fact]
    public void Deep_legal_composites_use_an_iterative_walk()
    {
        const int count = 30000;
        var tables = Tables(MakeFont());
        var glyf = new byte[count * 16];
        var loca = new byte[(count + 1) * 4];
        for (var i = 0; i < count; i++)
        {
            Put32(loca, i * 4, (uint)(i * 16));
            if (i > 0)
            {
                Put16(glyf, i * 16, 65535);
                Put16(glyf, (i * 16) + 10, 2); // byte XY arguments
                Put16(glyf, (i * 16) + 12, i - 1);
            }
        }

        Put32(loca, count * 4, (uint)glyf.Length);
        var maxp = tables["maxp"].ToArray();
        Put16(maxp, 4, count);
        Put16(maxp, 30, count);
        tables["glyf"] = glyf;
        tables["loca"] = loca;
        tables["maxp"] = maxp;
        tables["hmtx"] = new byte[4 + ((count - 1) * 2)];
        var result = TrueTypeSubsetter.Create(Write(tables), [count - 1], default);
        Assert.Equal(glyf, new SfntReader(result).Get("glyf").ToArray());
    }

    [Fact]
    public void Selected_gid_outside_numGlyphs_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => TrueTypeSubsetter.Create(MakeFont(), [9], default));

    [Fact]
    public void Small_corrupt_corpus_never_escapes_with_index_or_allocation_exceptions()
    {
        var random = new Random(427);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[random.Next(0, 1024)];
            random.NextBytes(bytes);
            var error = Record.Exception(() => TrueTypeSubsetter.Create(bytes, [2], default));
            Assert.True(error is InvalidDataException or NotSupportedException, error?.ToString());
        }
    }

    [Theory]
    [InlineData("collection-distinct.ttc")]
    [InlineData("collection-v2.ttc")]
    public void Extracts_nonzero_faces_with_shared_tables_and_regenerates_checksums(string name)
    {
        var bytes = Fixture(name);
        var copy = bytes.ToArray();
        var collection = new FontCollectionReader(bytes);
        Assert.Equal(2, collection.FaceOffsets.Length);
        var first = collection.ReadFace(0);
        var second = collection.ReadFace(1);
        Assert.Equal(first.Tables["glyf"], second.Tables["glyf"]);
        Assert.NotEqual(first.Tables["hmtx"], second.Tables["hmtx"]);
        var extracted = FontFaceExtractor.Extract(bytes, 1);
        Assert.Equal(extracted, FontFaceExtractor.Extract(bytes, 1));
        var standalone = new SfntReader(extracted);
        foreach (var table in second.Tables.Keys.Where(tag => tag != "head"))
        {
            Assert.Equal(second.Get(table).ToArray(), standalone.Get(table).ToArray());
        }

        Assert.Equal(copy, bytes);
        Assert.Equal(0xB1B0AFBAu, IndependentChecksum(extracted));
        ValidateDirectoryChecksums(extracted);
        var subset = TrueTypeSubsetter.Create(extracted, [2, 3], default);
        Assert.Equal(0xB1B0AFBAu, IndependentChecksum(subset));
    }

    [Fact]
    public void Unselected_unsupported_faces_do_not_reject_a_static_face_and_color_tables_are_retained()
    {
        var bytes = Fixture("collection-mixed.ttc");
        TrueTypeSubsetter.Create(FontFaceExtractor.Extract(bytes, 0), [2], default);
        foreach (var index in new[] { 1, 2 })
        {
            var extracted = FontFaceExtractor.Extract(bytes, index);
            Assert.Throws<NotSupportedException>(() => FontCapabilities.ValidatePdf(new SfntReader(extracted)));
        }

        foreach (var index in new[] { 3, 4 })
        {
            var extracted = FontFaceExtractor.Extract(bytes, index);
            var face = new SfntReader(extracted);
            Assert.True(FontCapabilities.ValidatePdf(face));
            Assert.True(face.Get("COLR").Length > 0);
            Assert.True(face.Get("CPAL").Length > 0);
            Assert.Throws<NotSupportedException>(() => TrueTypeSubsetter.Create(extracted, [2], default));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Invalid_collection_indices_are_distinct_argument_errors(int index) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FontFaceExtractor.Extract(Fixture("collection-distinct.ttc"), index));

    [Fact]
    public void Standalone_font_accepts_only_index_zero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FontFaceExtractor.Extract(Fixture("static.ttf"), 1));

    [Theory]
    [InlineData(8, uint.MaxValue)]
    [InlineData(12, uint.MaxValue)]
    [InlineData(16, 0u)]
    [InlineData(20, 0x44534947u)]
    [InlineData(24, uint.MaxValue)]
    [InlineData(28, uint.MaxValue)]
    public void Rejects_corrupt_collection_headers_and_dsig(int offset, uint value)
    {
        var bytes = Fixture("collection-v2.ttc");
        Put32(bytes, offset, value);
        Assert.Throws<InvalidDataException>(() => FontFaceExtractor.Extract(bytes, 1));
    }

    internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", name));

    internal static Dictionary<string, ReadOnlyMemory<byte>> Tables(byte[] bytes)
    {
        var reader = new SfntReader(bytes);
        return reader.Tables.ToDictionary(entry => entry.Key, entry => (ReadOnlyMemory<byte>)reader.Get(entry.Key).ToArray());
    }

    internal static byte[] Write(Dictionary<string, ReadOnlyMemory<byte>> tables) => SfntWriter.Write(0x00010000, tables, default);

    internal static byte[] MakeFont(bool shortLoca = false)
    {
        var tables = Tables(Fixture("static.ttf"));
        // Simple glyph: one point, instructions preserved, zero X/Y deltas.
        var simple = new byte[18];
        Put16(simple, 0, 1);
        Put16(simple, 6, 100);
        Put16(simple, 8, 100);
        Put16(simple, 10, 0);
        Put16(simple, 12, 0);
        simple[14] = 0x31;
        byte[] Component(ushort child, ushort flags)
        {
            var size = 14 + ((flags & 1) != 0 ? 4 : 2) + ((flags & 8) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);
            var data = new byte[size + ((flags & 0x100) != 0 ? 4 : 0)];
            Put16(data, 0, 65535);
            Put16(data, 6, 100);
            Put16(data, 8, 100);
            Put16(data, 10, flags);
            Put16(data, 12, child);
            // Zero scale is structurally legal; outline/metrics tests use real fonts separately.
            if ((flags & 0x100) != 0)
            {
                Put16(data, size, 2);
                data[size + 2] = 0x00;
                data[size + 3] = 0x01;
            }

            return data;
        }

        var two = Component(3, 0x21).Concat(Component(2, 0x202)[10..]).ToArray();
        var glyphs = new[] { simple, Array.Empty<byte>(), simple, simple, two, Component(4, 0x0008), Component(5, 0x0140), Component(6, 0x0080), simple };
        var glyf = glyphs.SelectMany(bytes => bytes).ToArray();
        var loca = new byte[(glyphs.Length + 1) * (shortLoca ? 2 : 4)];
        var offset = 0;
        for (var i = 0; i <= glyphs.Length; i++)
        {
            if (shortLoca) Put16(loca, i * 2, offset / 2);
            else Put32(loca, i * 4, (uint)offset);
            if (i < glyphs.Length) offset += glyphs[i].Length;
        }

        var head = tables["head"].ToArray();
        Put16(head, 50, shortLoca ? 0 : 1);
        var maxp = tables["maxp"].ToArray();
        Put16(maxp, 4, glyphs.Length);
        var hhea = tables["hhea"].ToArray();
        Put16(hhea, 34, 1);
        var hmtx = new byte[4 + ((glyphs.Length - 1) * 2)];
        Put16(hmtx, 0, 600);
        var post = new byte[32];
        Put32(post, 0, 0x00030000);
        tables["glyf"] = glyf;
        tables["loca"] = loca;
        tables["head"] = head;
        tables["maxp"] = maxp;
        tables["hhea"] = hhea;
        tables["hmtx"] = hmtx;
        tables["post"] = post;
        tables["DSIG"] = new byte[8];
        tables["GSUB"] = new byte[10];
        return Write(tables);
    }

    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static void Put16(byte[] data, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), (ushort)value);

    private static void Put32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);

    private static uint IndependentChecksum(byte[] bytes)
    {
        ulong sum = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            uint word = 0;
            for (var j = 0; j < 4; j++) word = (word << 8) | (i + j < bytes.Length ? bytes[i + j] : 0u);
            sum += word;
        }

        return unchecked((uint)sum);
    }

    private static void ValidateDirectoryChecksums(byte[] bytes)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var position = 12 + (i * 16);
            var offset = (int)Read32(bytes, position + 8);
            var length = (int)Read32(bytes, position + 12);
            Assert.Equal(0, offset % 4);
            var data = bytes.AsSpan(offset, length).ToArray();
            if (System.Text.Encoding.ASCII.GetString(bytes, position, 4) == "head") Array.Clear(data, 8, 4);
            Assert.Equal(Read32(bytes, position + 4), IndependentChecksum(data));
            var padding = ((length + 3) & ~3) - length;
            Assert.All(bytes.AsSpan(offset + length, padding).ToArray(), value => Assert.Equal(0, value));
        }
    }
}
