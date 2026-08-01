using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

public sealed class PdfSharpFontResolver : IFontResolver
{
    private readonly string _familyName;
    private readonly string _faceName;
    private readonly byte[] _fontData;

    public PdfSharpFontResolver(string familyName, string fontFilePath)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            throw new ArgumentException("フォントファミリー名は必須です。", nameof(familyName));
        if (string.IsNullOrWhiteSpace(fontFilePath))
            throw new ArgumentException("フォントファイルパスは必須です。", nameof(fontFilePath));

        _familyName = familyName;
        _faceName = Path.GetFullPath(fontFilePath);
        _fontData = File.ReadAllBytes(_faceName);
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        string.Equals(familyName, _familyName, StringComparison.OrdinalIgnoreCase)
            ? new FontResolverInfo(_faceName)
            : null;

    public byte[]? GetFont(string faceName) =>
        string.Equals(faceName, _faceName, StringComparison.Ordinal)
            ? _fontData
            : null;
}
