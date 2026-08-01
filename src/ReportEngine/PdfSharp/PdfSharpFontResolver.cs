using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ReportEngine.Abstractions;
using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;
using SkiaSharp;

namespace ReportEngine.PdfSharp;

public sealed class PdfSharpFontResolver : IFontResolver
{
    private readonly string _familyName;
    private readonly string _faceName;
    private readonly byte[] _fontData;

    public PdfSharpFontResolver(string familyName, string fontFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFilePath);

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
