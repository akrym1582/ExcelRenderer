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

/// <summary>PDFsharp から要求された書体をフォントファイルへ解決し、そのバイナリデータを提供します。</summary>
public sealed class PdfSharpFontResolver : IFontResolver
{
    private readonly IFontManager _manager;
    private readonly Dictionary<string, byte[]> _fontData = new();
    private readonly string? _legacyFamily;
    private readonly string? _legacyFace;
    private readonly string[] _familyAliases = [];

    /// <summary>Initializes a new instance of the <see cref="PdfSharpFontResolver"/> class. 単一のフォントファイルを指定したファミリー名と別名に割り当てる互換モードのリゾルバーを初期化します。</summary>
    /// <param name="familyName">フォントファイルを割り当てる正式なフォントファミリー名です。</param>
    /// <param name="fontFilePath">すべての書体要求に使用するフォントファイルのパスです。</param>
    /// <param name="familyAliases">同じフォントファイルへ解決する追加のファミリー名です。</param>
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

    /// <summary>Initializes a new instance of the <see cref="PdfSharpFontResolver"/> class. 指定したフォントマネージャーを使用して書体要求を解決するリゾルバーを初期化します。</summary>
    /// <param name="manager">フォント属性から実際のフォントファイルを選択するマネージャーです。</param>
    public PdfSharpFontResolver(IFontManager manager) => _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    /// <summary>PDFsharp のファミリー名と書体要求をフォントファイルに対応するフェイス名へ解決します。</summary>
    /// <param name="familyName">要求されたフォントファミリー名です。</param>
    /// <param name="bold">太字を要求する場合は <see langword="true"/> です。</param>
    /// <param name="italic">斜体を要求する場合は <see langword="true"/> です。</param>
    /// <returns>解決したフォントを識別するフェイス情報を返します。互換モードでファミリー名が登録名または別名に一致しない場合は <see langword="null"/> を返します。</returns>
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

    /// <summary>解決済みのフェイス名に対応するフォントファイルのバイナリデータを取得します。</summary>
    /// <param name="faceName"><see cref="ResolveTypeface"/> が返したフォントフェイス名です。</param>
    /// <returns>フォントファイルの全バイトを返します。フェイス名が未解決の場合は <see langword="null"/> を返します。</returns>
    public byte[]? GetFont(string faceName) =>
        _fontData.GetValueOrDefault(faceName);
}
