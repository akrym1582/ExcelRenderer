using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;
using ExcelRenderer.Fonts;

namespace ExcelRenderer.PdfSharp;

public sealed class PdfSharpFontResolver : IFontResolver
{
    private readonly IFontManager _manager;
    private readonly Dictionary<string, byte[]> _fontData = new();
    private readonly string? _legacyFamily;
    private readonly string? _legacyFace;

    public PdfSharpFontResolver(string familyName, string fontFilePath)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            throw new ArgumentException("フォントファミリー名は必須です。", nameof(familyName));
        if (string.IsNullOrWhiteSpace(fontFilePath))
            throw new ArgumentException("フォントファイルパスは必須です。", nameof(fontFilePath));

        _legacyFamily = familyName; _legacyFace = Path.GetFullPath(fontFilePath);
        var manager = new FontManager(); manager.Register(familyName, fontFilePath, fontFilePath, fontFilePath, fontFilePath); _manager = manager;
        _fontData[_legacyFace] = File.ReadAllBytes(_legacyFace);
    }

    public PdfSharpFontResolver(IFontManager manager) => _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (_legacyFace is not null)
            return string.Equals(familyName, _legacyFamily, StringComparison.OrdinalIgnoreCase) ? new FontResolverInfo(_legacyFace) : null;
        var font = _manager.Resolve(new(familyName, bold ? 700 : 400, italic));
        var face = $"{font.Family}|{font.Weight}|{(font.Italic ? "italic" : "normal")}|{font.FilePath}";
        if (!_fontData.ContainsKey(face)) _fontData[face] = File.ReadAllBytes(font.FilePath);
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName) =>
        _fontData.GetValueOrDefault(faceName);
}
