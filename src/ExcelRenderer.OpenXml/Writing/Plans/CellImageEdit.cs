using System;
using System.IO;

namespace ExcelRenderer.OpenXml.Writing.Plans;

/// <summary>An embedded raster image and its point-based placement relative to a cell.</summary>
public sealed class CellImageEdit
{
    /// <summary>Initializes a new instance of the <see cref="CellImageEdit"/> class.</summary>
    /// <param name="data">PNG or JPEG bytes.</param>
    /// <param name="width">Requested width in points, or null.</param>
    /// <param name="height">Requested height in points, or null.</param>
    /// <param name="scale">Original-size multiplier.</param>
    /// <param name="offsetX">Horizontal offset in points.</param>
    /// <param name="offsetY">Vertical offset in points.</param>
    /// <param name="contain">Whether to preserve aspect ratio inside both dimensions.</param>
    public CellImageEdit(byte[] data, double? width, double? height, double scale, double offsetX, double offsetY, bool contain)
    {
        Data = (byte[])data.Clone();
        var dimensions = Dimensions(Data);
        ContentType = dimensions.Png ? "image/png" : "image/jpeg";
        var naturalWidth = dimensions.Width * 0.75;
        var naturalHeight = dimensions.Height * 0.75;
        Width = width ?? (height.HasValue ? height.Value * naturalWidth / naturalHeight : naturalWidth * scale);
        Height = height ?? (width.HasValue ? width.Value * naturalHeight / naturalWidth : naturalHeight * scale);
        if (contain && width.HasValue && height.HasValue)
        {
            var ratio = Math.Min(width.Value / naturalWidth, height.Value / naturalHeight);
            Width = naturalWidth * ratio;
            Height = naturalHeight * ratio;
        }

        OffsetX = offsetX;
        OffsetY = offsetY;
        if (!Finite(Width) || !Finite(Height) || Width <= 0 || Height <= 0 || !Finite(offsetX) || !Finite(offsetY) || !Finite(scale) || scale <= 0)
        {
            throw new ArgumentException("Image dimensions must be positive and all geometry must be finite.");
        }
    }

    /// <summary>Gets the embedded bytes.</summary>
    public byte[] Data { get; }

    /// <summary>Gets the detected content type.</summary>
    public string ContentType { get; }

    /// <summary>Gets the final width in points.</summary>
    public double Width { get; }

    /// <summary>Gets the final height in points.</summary>
    public double Height { get; }

    /// <summary>Gets the horizontal offset in points.</summary>
    public double OffsetX { get; }

    /// <summary>Gets the vertical offset in points.</summary>
    public double OffsetY { get; }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < long.MaxValue / 12700.0;

    private static (int Width, int Height, bool Png) Dimensions(byte[] data)
    {
        if (data.Length >= 33 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 && data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10)
        {
            var width = Read32(data, 16);
            var height = Read32(data, 20);
            if (Read32(data, 8) != 13 || data[12] != 73 || data[13] != 72 || data[14] != 68 || data[15] != 82 || width <= 0 || height <= 0)
            {
                throw new InvalidDataException("Invalid PNG header.");
            }

            var position = 8;
            var hasData = false;
            while (position <= data.Length - 12)
            {
                var length = Read32(data, position);
                if (length < 0 || length > data.Length - position - 12)
                {
                    break;
                }

                var checksum = uint.MaxValue;
                for (var index = position + 4; index < position + length + 8; index++)
                {
                    checksum ^= data[index];
                    for (var bit = 0; bit < 8; bit++)
                    {
                        checksum = (checksum >> 1) ^ ((checksum & 1) != 0 ? 0xEDB88320U : 0U);
                    }
                }

                if (~checksum != unchecked((uint)Read32(data, position + length + 8)))
                {
                    throw new InvalidDataException("Invalid PNG checksum.");
                }

                var type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
                hasData |= type == "IDAT" && length > 0;
                if (type == "IEND" && length == 0 && hasData)
                {
                    return (width, height, true);
                }

                position += length + 12;
            }

            throw new InvalidDataException("Incomplete PNG image.");
        }

        if (data.Length >= 4 && data[0] == 255 && data[1] == 216 && data[data.Length - 2] == 255 && data[data.Length - 1] == 217)
        {
            var position = 2;
            while (position < data.Length - 3)
            {
                if (data[position++] != 255)
                {
                    break;
                }

                while (position < data.Length && data[position] == 255)
                {
                    position++;
                }

                if (position >= data.Length - 2)
                {
                    break;
                }

                var marker = data[position++];
                var length = (data[position] << 8) | data[position + 1];
                if (length < 2 || length > data.Length - position)
                {
                    break;
                }

                if (marker is >= 192 and <= 207 && marker is not 196 and not 200 and not 204 && length >= 8)
                {
                    var height = (data[position + 3] << 8) | data[position + 4];
                    var width = (data[position + 5] << 8) | data[position + 6];
                    if (width > 0 && height > 0)
                    {
                        return (width, height, false);
                    }
                }

                position += length;
            }
        }

        throw new InvalidDataException("Expected a complete PNG or JPEG image.");
    }

    private static int Read32(byte[] data, int offset) => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
