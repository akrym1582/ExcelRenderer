namespace ExcelRenderer.Fonts;

public sealed record FontRequest(string Family, int Weight = 400, bool Italic = false);
public sealed record ResolvedFont(string Family, int Weight, bool Italic, string FilePath);
public sealed class FontOptions
{
    public IReadOnlyList<string> FallbackFamilies { get; init; } = ["Noto Sans JP", "Noto Sans", "Liberation Sans"];
    public IReadOnlyList<string> FontDirectories { get; init; } = [];
}
public interface IFontManager { ResolvedFont Resolve(FontRequest request); }

/// <summary>Cross-platform, deterministic font-face catalog shared by renderers.</summary>
public sealed class FontManager : IFontManager
{
    private readonly FontOptions _options;
    private readonly Dictionary<(string Family, int Weight, bool Italic), string> _faces = new();
    private readonly Dictionary<FontRequest, ResolvedFont> _cache = new();
    public FontManager(FontOptions? options = null) { _options = options ?? new(); Scan(); }
    public void Register(string family, string regular, string? bold = null, string? italic = null, string? boldItalic = null)
    {
        Add(family, 400, false, regular); Add(family, 700, false, bold); Add(family, 400, true, italic); Add(family, 700, true, boldItalic);
        _cache.Clear();
    }
    public ResolvedFont Resolve(FontRequest request)
    {
        if (_cache.TryGetValue(request, out var cached)) return cached;
        foreach (var family in new[] { request.Family }.Concat(_options.FallbackFamilies))
        {
            var candidates = _faces.Where(x => string.Equals(x.Key.Family, family, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0) continue;
            var selected = candidates.OrderBy(x => x.Key.Italic == request.Italic ? 0 : 1).ThenBy(x => Math.Abs(x.Key.Weight-request.Weight)).First();
            return _cache[request] = new(selected.Key.Family, selected.Key.Weight, selected.Key.Italic, selected.Value);
        }
        var any = _faces.FirstOrDefault();
        if (!string.IsNullOrEmpty(any.Value)) return _cache[request] = new(any.Key.Family, any.Key.Weight, any.Key.Italic, any.Value);
        throw new InvalidOperationException($"No usable font was found. Requested: \"{request.Family}\" Fallbacks: {string.Join(", ", _options.FallbackFamilies)}");
    }
    private void Add(string family, int weight, bool italic, string? path) { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) _faces[(family,weight,italic)] = Path.GetFullPath(path); }
    private void Scan()
    {
        var dirs = _options.FontDirectories.Concat(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? [Environment.GetFolderPath(Environment.SpecialFolder.Fonts)]
            : new[] { "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts") });
        foreach (var dir in dirs.Where(Directory.Exists)) foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(x => x.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            using var typeface = global::SkiaSharp.SKTypeface.FromFile(file);
            if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName)) continue;
            var style = typeface.FontStyle;
            var italic = style.Slant != global::SkiaSharp.SKFontStyleSlant.Upright;
            _faces.TryAdd((typeface.FamilyName, style.Weight, italic), file);
        }
    }
}
