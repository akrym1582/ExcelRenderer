using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>同梱した Noto Serif CJK JP Regular を提供します。</summary>
internal static class BundledJapaneseSerifFont
{
    /// <summary>Gets the stable in-memory face name.</summary>
    internal const string FaceName = "ExcelRenderer.NotoSerifCJKjp-Regular";

    private static readonly Lazy<byte[]?> FontData = new(() =>
    {
        using var stream = typeof(BundledJapaneseSerifFont).Assembly.GetManifestResourceStream("ExcelRenderer.NotoSerifCJKjp-Regular.otf");
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
