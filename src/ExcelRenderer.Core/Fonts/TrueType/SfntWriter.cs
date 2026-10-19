using System.Buffers.Binary;
using System.Text;

namespace ExcelRenderer.Core.Fonts.TrueType;

/// <summary>Writes deterministic, aligned sfnt tables and independent checksums.</summary>
internal static class SfntWriter
{
    /// <summary>Writes a standalone face.</summary>
    /// <param name="version">The original outline version.</param>
    /// <param name="tables">The unpadded table data.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The standalone sfnt.</returns>
    internal static byte[] Write(uint version, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> tables, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ordered = tables.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        long size = 12 + (ordered.Length * 16);
        foreach (var entry in ordered)
        {
            size += ((long)entry.Value.Length + 3) & ~3L;
        }

        if (ordered.Length == 0 || ordered.Length > 4095 || size > int.MaxValue - 3)
        {
            throw SfntReader.Invalid("sfnt", "output size exceeds managed bounds");
        }

        var output = new byte[(int)size];
        Put32(output, 0, version);
        Put16(output, 4, ordered.Length);
        var power = 1;
        var selector = 0;
        while (power * 2 <= ordered.Length)
        {
            power *= 2;
            selector++;
        }

        Put16(output, 6, power * 16);
        Put16(output, 8, selector);
        Put16(output, 10, (ordered.Length - power) * 16);
        var offset = 12 + (ordered.Length * 16);
        var headOffset = -1;
        for (var i = 0; i < ordered.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = ordered[i];
            var position = 12 + (i * 16);
            Encoding.ASCII.GetBytes(entry.Key).CopyTo(output, position);
            entry.Value.Span.CopyTo(output.AsSpan(offset));
            if (entry.Key == "head")
            {
                SfntReader.Range(entry.Value.Length, 0, 12, "head");
                headOffset = offset;
                Put32(output, offset + 8, 0);
            }

            Put32(output, position + 4, Checksum(output.AsSpan(offset, entry.Value.Length), token));
            Put32(output, position + 8, (uint)offset);
            Put32(output, position + 12, (uint)entry.Value.Length);
            offset += (entry.Value.Length + 3) & ~3;
        }

        if (headOffset < 0)
        {
            throw SfntReader.Invalid("head", "required for standalone checksum adjustment");
        }

        Put32(output, headOffset + 8, unchecked(0xB1B0AFBAu - Checksum(output, token)));
        return output;
    }

    /// <summary>Writes a big-endian word.</summary>
    /// <param name="data">The destination.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="value">The word.</param>
    internal static void Put16(Span<byte> data, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), checked((ushort)value));

    /// <summary>Writes a big-endian integer.</summary>
    /// <param name="data">The destination.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="value">The integer.</param>
    internal static void Put32(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);

    private static uint Checksum(ReadOnlySpan<byte> bytes, CancellationToken token)
    {
        uint sum = 0;
        var position = 0;
        for (; position <= bytes.Length - 4; position += 4)
        {
            if ((position & 0xffff) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(position, 4)));
        }

        uint tail = 0;
        for (var i = 0; position + i < bytes.Length; i++)
        {
            tail |= (uint)bytes[position + i] << (24 - (i * 8));
        }

        sum = unchecked(sum + tail);

        return sum;
    }
}
