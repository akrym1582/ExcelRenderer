using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

/// <summary>
    /// Initializes a new instance of the class. 必要な設定を使って新しいインスタンスを初期化します.
/// </summary>
public sealed class PdfSharpFontResolver : IFontResolver
{
    private readonly IFontManager _manager;
    private readonly Dictionary<string, byte[]> _fontData = new();
    private readonly string? _legacyFamily;
    private readonly string? _legacyFace;
    private readonly string[] _familyAliases = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfSharpFontResolver"/> class. 必要な設定を使用して新しいインスタンスを初期化します.
    /// </summary>
    /// <param name="familyAliases">familyAliases に渡す値です。</param>
    /// <param name="familyName">familyName に渡す値です。</param>
    /// <param name="fontFilePath">fontFilePath に渡す値です。</param>
    public PdfSharpFontResolver(string familyName, string fontFilePath, params string[] familyAliases)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            throw new ArgumentException("フォントファミリー名は必須です。", nameof(familyName));
        }

        if (string.IsNullOrWhiteSpace(fontFilePath))
        {
            throw new ArgumentException("フォントファイルパスは必須です。", nameof(fontFilePath));
        }

        _legacyFamily = familyName;
        _legacyFace = Path.GetFullPath(fontFilePath);
        _familyAliases = familyAliases.ToArray();
        var manager = new FontManager();
        manager.Register(familyName, fontFilePath, fontFilePath, fontFilePath, fontFilePath);
        _manager = manager;
        _fontData[_legacyFace] = File.ReadAllBytes(_legacyFace);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfSharpFontResolver"/> class. 必要な設定を使用して新しいインスタンスを初期化します.
    /// </summary>
    /// <param name="manager">manager に渡す値です。</param>
    public PdfSharpFontResolver(IFontManager manager) => _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    /// <summary>
    /// ResolveTypeface を実行します.
    /// </summary>
    /// <param name="familyName">familyName に渡す値です。</param>
    /// <param name="bold">bold に渡す値です。</param>
    /// <param name="italic">italic に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (_legacyFace is not null)
        {
            return string.Equals(familyName, _legacyFamily, StringComparison.OrdinalIgnoreCase) ||
                _familyAliases.Contains(familyName, StringComparer.OrdinalIgnoreCase)
                ? new FontResolverInfo(_legacyFace) : null;
        }

        var font = _manager.Resolve(new(familyName, bold ? 700 : 400, italic));
        var face = $"{font.Family}|{font.Weight}|{(font.Italic ? "italic" : "normal")}|{font.FilePath}";
        if (!_fontData.ContainsKey(face))
        {
            _fontData[face] = File.ReadAllBytes(font.FilePath);
        }

        return new FontResolverInfo(face);
    }

    /// <summary>
    /// GetFont を実行します.
    /// </summary>
    /// <param name="faceName">faceName に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public byte[]? GetFont(string faceName) =>
        _fontData.GetValueOrDefault(faceName);
}
