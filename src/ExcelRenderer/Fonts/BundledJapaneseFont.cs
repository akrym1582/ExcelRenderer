using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>内蔵の Noto Sans JP Regular フォントを共有します。</summary>
internal static class BundledJapaneseFont
{
    /// <summary>PDFsharp が内蔵フォントを識別するフェイス名です。</summary>
    internal const string FaceName = "ExcelRenderer.NotoSansJP-Regular";

    private static readonly Lazy<byte[]?> FontData = new(() =>
    {
        using var stream = typeof(BundledJapaneseFont).Assembly.GetManifestResourceStream("ExcelRenderer.NotoSansJP-Regular.ttf");
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    });

    private static readonly Lazy<SKTypeface?> TypefaceValue = new(() =>
    {
        if (Data is not { } data)
        {
            return null;
        }

        using var stream = new MemoryStream(data, writable: false);
        return SKTypeface.FromStream(stream);
    });

    /// <summary>Gets the bundled font bytes. 内蔵フォントのバイト列です。</summary>
    internal static byte[]? Data => FontData.Value;

    /// <summary>Gets the font face used for PNG rendering. PNG 描画に使用するフォントフェイスです。</summary>
    internal static SKTypeface? Typeface => TypefaceValue.Value;
}
