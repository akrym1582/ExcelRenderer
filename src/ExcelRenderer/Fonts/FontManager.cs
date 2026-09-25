namespace ExcelRenderer.Fonts;

/// <summary>登録済みおよびシステム上のフォントを検索し、描画要求に最も近いフォントファイルを解決します。</summary>
public sealed class FontManager : IFontManager
{
    private readonly FontOptions _options;
    private readonly Dictionary<(string Family, int Weight, bool Italic), string> _faces = new();
    private readonly Dictionary<FontRequest, ResolvedFont> _cache = new();

    /// <summary>Initializes a new instance of the <see cref="FontManager"/> class. 指定した検索設定を使用してフォントファイルを走査し、フォントマネージャーを初期化します。</summary>
    /// <param name="options">フォールバックファミリーと追加の検索ディレクトリです。省略時は既定の設定を使用します。</param>
    public FontManager(FontOptions? options = null)
    {
        _options = options ?? new();
        Scan();
    }

    /// <summary>フォントファミリーの標準、太字、斜体、および太字斜体のファイルをカタログへ登録します。</summary>
    /// <param name="family">登録するフォントファミリー名です。</param>
    /// <param name="regular">標準書体のフォントファイルパスです。</param>
    /// <param name="bold">太字書体のフォントファイルパスです。未指定または存在しないファイルは登録しません。</param>
    /// <param name="italic">斜体書体のフォントファイルパスです。未指定または存在しないファイルは登録しません。</param>
    /// <param name="boldItalic">太字斜体書体のフォントファイルパスです。未指定または存在しないファイルは登録しません。</param>
    public void Register(string family, string regular, string? bold = null, string? italic = null, string? boldItalic = null)
    {
        Add(family, 400, false, regular);
        Add(family, 700, false, bold);
        Add(family, 400, true, italic);
        Add(family, 700, true, boldItalic);
        _cache.Clear();
    }

    /// <summary>要求されたファミリー、斜体、ウェイトに最も近い登録済みフォントファイルを選択します。</summary>
    /// <param name="request">希望するフォントファミリー、ウェイト、および斜体の有無です。</param>
    /// <returns>実際に選択されたファミリー、ウェイト、斜体の有無、およびフォントファイルの絶対パスを返します。</returns>
    public ResolvedFont Resolve(FontRequest request)
    {
        if (_cache.TryGetValue(request, out var cached))
        {
            return cached;
        }

        foreach (var family in new[] { request.Family }.Concat(_options.FallbackFamilies))
        {
            var candidates = _faces.Where(x => string.Equals(x.Key.Family, family, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            var selected = candidates.OrderBy(x => x.Key.Italic == request.Italic ? 0 : 1).ThenBy(x => Math.Abs(x.Key.Weight - request.Weight)).First();
            return _cache[request] = new(selected.Key.Family, selected.Key.Weight, selected.Key.Italic, selected.Value);
        }

        var any = _faces.FirstOrDefault();
        if (!string.IsNullOrEmpty(any.Value))
        {
            return _cache[request] = new(any.Key.Family, any.Key.Weight, any.Key.Italic, any.Value);
        }

        throw new InvalidOperationException($"No usable font was found. Requested: \"{request.Family}\" Fallbacks: {string.Join(", ", _options.FallbackFamilies)}");
    }

    private void Add(string family, int weight, bool italic, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            _faces[(family, weight, italic)] = Path.GetFullPath(path);
        }
    }

    private void Scan()
    {
        var dirs = _options.FontDirectories.Concat(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? [Environment.GetFolderPath(Environment.SpecialFolder.Fonts)]
            : new[] { "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts") });
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(x => x.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            using var typeface = global::SkiaSharp.SKTypeface.FromFile(file);
            if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName))
                {
                    continue;
                }

            var style = typeface.FontStyle;
            var italic = style.Slant != global::SkiaSharp.SKFontStyleSlant.Upright;
            _faces.TryAdd((typeface.FamilyName, style.Weight, italic), file);
        }
        }
    }
}
