using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
    private const string ResolvedFacePrefix = "excel-renderer-face:";
    private static readonly ConcurrentDictionary<string, WeakReference<byte[]>> ResolvedFontData = new();
    private static readonly ConditionalWeakTable<ResolvedFont, Lazy<RegisteredFont>> ResolvedFonts = new();
    private static int _resolvedFontHashCount;
    private static int _resolvedFontReadCount;
    private readonly IFontManager? _manager;
    private readonly Dictionary<string, byte[]> _fontData = new();
    private readonly string? _legacyFamily;
    private readonly string? _legacyFace;
    private readonly string[] _familyAliases = [];
    private readonly Lazy<IFontManager> _systemFontManager = new(() => new FontManager());

    /// <summary>Initializes a new instance of the <see cref="PdfSharpFontResolver"/> class. 使用可能なフォントを解決するリゾルバーを初期化します。</summary>
    public PdfSharpFontResolver()
    {
    }

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

    /// <summary>Gets process-lifetime registration work counters for regression tests.</summary>
    internal static (int Reads, int Hashes) RegistrationWork =>
        (Volatile.Read(ref _resolvedFontReadCount), Volatile.Read(ref _resolvedFontHashCount));

    /// <summary>PDFsharp のファミリー名と書体要求をフォントファイルに対応するフェイス名へ解決します。</summary>
    /// <param name="familyName">要求されたフォントファミリー名です。</param>
    /// <param name="bold">太字を要求する場合は <see langword="true"/> です。</param>
    /// <param name="italic">斜体を要求する場合は <see langword="true"/> です。</param>
    /// <returns>解決したフォントを識別するフェイス情報を返します。互換モードでファミリー名が登録名または別名に一致しない場合は <see langword="null"/> を返します。</returns>
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (familyName.StartsWith(ResolvedFacePrefix, StringComparison.Ordinal))
        {
            return GetRegisteredData(familyName) is not null ? new FontResolverInfo(familyName) : null;
        }

        if (_legacyFace is not null)
        {
            return string.Equals(familyName, _legacyFamily, StringComparison.OrdinalIgnoreCase) ||
                _familyAliases.Contains(familyName, StringComparer.OrdinalIgnoreCase)
                ? new FontResolverInfo(_legacyFace) : null;
        }

        var font = (_manager ?? _systemFontManager.Value).Resolve(new(familyName, bold ? 700 : 400, italic));
        var face = $"{font.Family}|{font.Weight}|{(font.Italic ? "italic" : "normal")}|{font.FilePath}";
        if (!_fontData.ContainsKey(face))
        {
            _fontData[face] = font.FontData ?? File.ReadAllBytes(font.FilePath);
        }

        return new FontResolverInfo(face);
    }

    /// <summary>解決済みのフェイス名に対応するフォントファイルのバイナリデータを取得します。</summary>
    /// <param name="faceName"><see cref="ResolveTypeface"/> が返したフォントフェイス名です。</param>
    /// <returns>フォントファイルの全バイトを返します。フェイス名が未解決の場合は <see langword="null"/> を返します。</returns>
    public byte[]? GetFont(string faceName) =>
        GetRegisteredData(faceName) ?? _fontData.GetValueOrDefault(faceName);

    /// <summary>Registers an already resolved physical face and returns its PDFsharp family key.</summary>
    /// <param name="font">The physical face selected during layout.</param>
    /// <returns>A unique internal PDFsharp family key.</returns>
    internal static string RegisterResolvedFont(ResolvedFont font)
    {
        if (font is null)
        {
            throw new ArgumentNullException(nameof(font));
        }

        return ResolvedFonts.GetValue(
            font,
            static value => new Lazy<RegisteredFont>(
                () => RegisterCore(value),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value.Key;
    }

    /// <summary>Removes weak registrations whose public font owners have expired.</summary>
    internal static void RemoveExpiredRegistrations()
    {
        foreach (var pair in ResolvedFontData)
        {
            if (!pair.Value.TryGetTarget(out _))
            {
                ResolvedFontData.TryRemove(pair.Key, out _);
            }
        }
    }

    private static byte[]? GetRegisteredData(string key) => ResolvedFontData.TryGetValue(key, out var reference) && reference.TryGetTarget(out var bytes) ? bytes : null;

    private static RegisteredFont RegisterCore(ResolvedFont font)
    {
        byte[] source;
        if (font.FontData is null)
        {
            Interlocked.Increment(ref _resolvedFontReadCount);
            source = File.ReadAllBytes(font.FilePath);
        }
        else
        {
            source = font.FontData;
        }

        // Own the bytes used by the digest so mutations of a caller-owned array cannot
        // change the data behind an already-issued PDFsharp key.
        var snapshot = source.ToArray();
        Interlocked.Increment(ref _resolvedFontHashCount);
        using var sha256 = SHA256.Create();
        var digest = BitConverter.ToString(sha256.ComputeHash(snapshot)).Replace("-", string.Empty, StringComparison.Ordinal);
        var key = $"{ResolvedFacePrefix}{font.FaceId}:{digest}";
        var reference = ResolvedFontData.AddOrUpdate(key, _ => new(snapshot), (registrationKey, existing) => existing.TryGetTarget(out _) ? existing : new(snapshot));
        return new(key, reference.TryGetTarget(out var registered) ? registered : snapshot);
    }

    private sealed record RegisteredFont(string Key, byte[] Data);
}
