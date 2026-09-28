using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>同梱した IPAmj 明朝を IVS 用フォールバックとして提供します。</summary>
internal static class BundledIvsFont
{
    /// <summary>Gets the stable in-memory face name.</summary>
    internal const string FaceName = "ExcelRenderer.IPAmjMincho";

    private static readonly Lazy<byte[]?> FontData = new(() =>
    {
        using var stream = typeof(BundledIvsFont).Assembly.GetManifestResourceStream("ExcelRenderer.ipamjm.ttf");
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    });

    /// <summary>Gets the embedded font bytes, or <see langword="null"/> when unavailable.</summary>
    internal static byte[]? Data => FontData.Value;

    /// <summary>Gets the family name stored in the embedded font.</summary>
    internal static string? FamilyName
    {
        get
        {
            if (Data is not { } data)
            {
                return null;
            }

            using var stream = new MemoryStream(data, writable: false);
            using var typeface = SKTypeface.FromStream(stream);
            return typeface?.FamilyName;
        }
    }
}
