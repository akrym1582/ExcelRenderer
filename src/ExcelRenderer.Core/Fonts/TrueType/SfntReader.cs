using System.Buffers.Binary;
using System.Text;

namespace ExcelRenderer.Core.Fonts.TrueType;

/// <summary>Reads a bounded sfnt directory without trusting file offsets.</summary>
internal sealed class SfntReader
{
    /// <summary>Initializes a new instance of the <see cref="SfntReader"/> class.</summary>
    /// <param name="data">The container bytes.</param>
    /// <param name="offset">The face directory offset.</param>
    /// <param name="reserved">Container header and face directory ranges.</param>
    /// <param name="token">The cancellation token.</param>
    internal SfntReader(byte[] data, int offset = 0, IReadOnlyList<(int Start, int Length)>? reserved = null, CancellationToken token = default)
    {
        Data = data;
        Range(data.Length, offset, 12, "sfnt header");
        Version = U32(data, offset);
        if (Version is not (0x00010000 or 0x4f54544f or 0x74727565 or 0x74797031))
        {
            var format = Version switch { 0x774f4646 => "WOFF", 0x774f4632 => "WOFF2", _ => $"0x{Version:X8}" };
            throw new NotSupportedException($"Unsupported font format {format}; a selected sfnt face is required.");
        }

        var count = U16(data, offset + 4);
        if (count == 0 || count > 4095)
        {
            throw Invalid("sfnt", "invalid table count");
        }

        var directoryLength = 12 + (count * 16);
        Range(data.Length, offset, directoryLength, "sfnt directory");
        var ranges = new List<(int Start, int Length)>();
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var position = offset + 12 + (i * 16);
            var tag = Encoding.ASCII.GetString(data, position, 4);
            var start = Offset(data, position + 8, tag);
            var length = Offset(data, position + 12, tag);
            Range(data.Length, start, length, tag);
            if (!Tables.TryAdd(tag, new(start, length)))
            {
                throw Invalid(tag, "duplicate table tag");
            }

            if (length == 0)
            {
                continue;
            }

            if ((start & 3) != 0 || Overlaps(start, length, offset, directoryLength) ||
                (reserved is not null && reserved.Any(range => Overlaps(start, length, range.Start, range.Length))))
            {
                throw Invalid(tag, "unaligned table or collision with container/directory");
            }

            ranges.Add((start, length));
        }

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (var i = 1; i < ranges.Count; i++)
        {
            if (Overlaps(ranges[i - 1].Start, ranges[i - 1].Length, ranges[i].Start, ranges[i].Length))
            {
                throw Invalid("sfnt", "overlapping tables in the selected face");
            }
        }
    }

    /// <summary>Gets the borrowed container bytes.</summary>
    internal byte[] Data { get; }

    /// <summary>Gets the outline version.</summary>
    internal uint Version { get; }

    /// <summary>Gets the bounded table entries.</summary>
    internal Dictionary<string, Table> Tables { get; } = new(StringComparer.Ordinal);

    /// <summary>Reads an unsigned big-endian word.</summary>
    /// <param name="data">The bounded bytes.</param>
    /// <param name="offset">The relative offset.</param>
    /// <returns>The word.</returns>
    internal static ushort U16(ReadOnlySpan<byte> data, int offset)
    {
        Range(data.Length, offset, 2, "sfnt");
        return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }

    /// <summary>Reads a signed big-endian word.</summary>
    /// <param name="data">The bounded bytes.</param>
    /// <param name="offset">The relative offset.</param>
    /// <returns>The word.</returns>
    internal static short I16(ReadOnlySpan<byte> data, int offset) => unchecked((short)U16(data, offset));

    /// <summary>Reads an unsigned big-endian integer.</summary>
    /// <param name="data">The bounded bytes.</param>
    /// <param name="offset">The relative offset.</param>
    /// <returns>The integer.</returns>
    internal static uint U32(ReadOnlySpan<byte> data, int offset)
    {
        Range(data.Length, offset, 4, "sfnt");
        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }

    /// <summary>Reads an offset that can be represented by a managed array.</summary>
    /// <param name="data">The bounded bytes.</param>
    /// <param name="offset">The relative offset.</param>
    /// <param name="table">The diagnostic table.</param>
    /// <returns>The array offset.</returns>
    internal static int Offset(ReadOnlySpan<byte> data, int offset, string table)
    {
        var value = U32(data, offset);
        return value <= int.MaxValue ? (int)value : throw Invalid(table, "offset/length exceeds managed bounds");
    }

    /// <summary>Checks a range before slicing or allocating.</summary>
    /// <param name="size">The containing size.</param>
    /// <param name="start">The start offset.</param>
    /// <param name="length">The range length.</param>
    /// <param name="table">The diagnostic table.</param>
    internal static void Range(int size, int start, int length, string table)
    {
        if (start < 0 || length < 0 || start > size || length > size - start)
        {
            throw Invalid(table, "truncated or out-of-range data");
        }
    }

    /// <summary>Creates a table-specific data diagnostic.</summary>
    /// <param name="table">The table.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>The exception.</returns>
    internal static InvalidDataException Invalid(string table, string reason) => new($"Invalid font table '{table}': {reason}.");

    /// <summary>Gets a bounded required table.</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="minimum">The minimum length.</param>
    /// <returns>The borrowed table bytes.</returns>
    internal ReadOnlySpan<byte> Get(string tag, int minimum = 0)
    {
        if (!Tables.TryGetValue(tag, out var table) || table.Length < minimum)
        {
            throw Invalid(tag, "missing or truncated required table");
        }

        return Data.AsSpan(table.Offset, table.Length);
    }

    private static bool Overlaps(int start, int length, int otherStart, int otherLength) =>
        (long)start < (long)otherStart + otherLength && (long)otherStart < (long)start + length;

    /// <summary>Identifies a bounded table in the container.</summary>
    /// <param name="Offset">The absolute offset.</param>
    /// <param name="Length">The unpadded length.</param>
    internal sealed record Table(int Offset, int Length);
}
