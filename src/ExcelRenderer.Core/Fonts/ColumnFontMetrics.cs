using System.Text;
using ExcelRenderer.Core.Fonts.TrueType;
using SkiaSharp;

namespace ExcelRenderer.Core.Fonts;

/// <summary>Measures source column fonts and verifies their physical family identity.</summary>
internal static class ColumnFontMetrics
{
    /// <summary>Measures the maximum digit advance at the Excel reference DPI.</summary>
    /// <param name="typeface">The physical source face.</param>
    /// <param name="size">The workbook Normal size in points.</param>
    /// <param name="libreOfficeCompatible">Whether to retain twip precision.</param>
    /// <returns>The maximum digit width in pixels at 96 DPI.</returns>
    internal static double Measure(SKTypeface typeface, double size, bool libreOfficeCompatible = false)
    {
        using var font = new SKFont(typeface, (float)(size * 96 / 72));
        if (libreOfficeCompatible)
        {
            font.LinearMetrics = true;
            font.Subpixel = true;
            font.Hinting = SKFontHinting.None;
        }

        var scale = libreOfficeCompatible ? 15 : 1;
        return Math.Max(1, Math.Round(Enumerable.Range(0, 10).Max(digit => font.MeasureText(digit.ToString())) * scale, MidpointRounding.AwayFromZero) / scale);
    }

    /// <summary>Checks physical family names, including localized names.</summary>
    /// <param name="typeface">The selected physical face.</param>
    /// <param name="family">The workbook family.</param>
    /// <returns>Whether the face declares this family.</returns>
    internal static bool MatchesFamily(SKTypeface typeface, string family) =>
        string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase) || HasFamilyName(typeface.GetTableData(0x6e616d65), family);

    /// <summary>Checks a font name table without retaining native resources.</summary>
    /// <param name="table">The sfnt name table.</param>
    /// <param name="family">The requested family.</param>
    /// <returns>Whether a Unicode family record matches.</returns>
    internal static bool HasFamilyName(byte[] table, string family)
    {
        if (table.Length < 6)
        {
            return false;
        }

        int Read(int offset) => (table[offset] << 8) | table[offset + 1];
        var count = Read(2);
        var strings = Read(4);
        for (var index = 0; index < count && 6 + ((index + 1) * 12) <= table.Length; index++)
        {
            var record = 6 + (index * 12);
            var platform = Read(record);
            var name = Read(record + 6);
            var length = Read(record + 8);
            var start = strings + Read(record + 10);
            if (name is not (1 or 16) || platform is not (0 or 3) || (length % 2) != 0 || start + length > table.Length)
            {
                continue;
            }

            if (string.Equals(Encoding.BigEndianUnicode.GetString(table, start, length), family, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Loads an explicit measurement face without applying PDF drawing restrictions.</summary>
    /// <param name="path">The source font path.</param>
    /// <param name="collectionIndex">The selected collection face.</param>
    /// <returns>A validated standalone font snapshot.</returns>
    internal static byte[] Load(string path, int collectionIndex)
    {
        try
        {
            var bytes = FontFaceExtractor.Extract(File.ReadAllBytes(path), collectionIndex);
            using var stream = new MemoryStream(bytes, writable: false);
            using var face = SKTypeface.FromStream(stream);
            if (face is null)
            {
                throw new InvalidDataException("Unsupported source font data.");
            }

            return bytes;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"Invalid SourceFontFile or SourceFontCollectionIndex: '{path}', face {collectionIndex}.", "SourceFontFile", error);
        }
    }
}
