using System.Buffers.Binary;
using SkiaSharp;

namespace ExcelRenderer.Core.Tests;

/// <summary>Deterministic complete-header images and payload corruption for image review regressions.</summary>
internal static class TestImages
{
    internal static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, bool alpha = false, int seed = 17)
    {
        using var bitmap = new SKBitmap(800, 800, SKColorType.Bgra8888, alpha ? SKAlphaType.Premul : SKAlphaType.Opaque);
        var pixels = new SKColor[800 * 800];
        uint state = (uint)seed;
        for (var i = 0; i < pixels.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            pixels[i] = new((byte)state, (byte)(state >> 8), (byte)(state >> 16), alpha ? (byte)(64 + state % 192) : (byte)255);
        }

        bitmap.Pixels = pixels;
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    internal static byte[] CorruptPng()
    {
        var bytes = Image();
        for (var offset = 8; offset + 12 <= bytes.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                // Keep the zlib header, but use the reserved deflate block type.
                bytes[offset + 10] = 0xff;
                var crc = 0xffffffffu;
                foreach (var value in bytes.AsSpan(offset + 4, length + 4))
                {
                    crc ^= value;
                    for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
                }

                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 8 + length, 4), ~crc);
                return bytes;
            }

            offset += length + 12;
        }

        throw new InvalidDataException("Generated PNG has no IDAT chunk.");
    }
}
