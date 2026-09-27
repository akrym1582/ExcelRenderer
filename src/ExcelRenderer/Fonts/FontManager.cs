using System.Globalization;
using System.Security.Cryptography;
using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>登録済み、追加ディレクトリ、および許可されたシステムフォントを決定的に解決します。</summary>
public sealed class FontManager : IFontManager
{
    private readonly FontOptions _options;
    private readonly List<FontFace> _faces = [];
    private readonly Dictionary<FontRequest, ResolvedFont> _cache = new();

    /// <summary>Initializes a new instance of the <see cref="FontManager"/> class.</summary>
    public FontManager(FontOptions? options = null)
    {
        _options = options ?? new();
        foreach (var registration in _options.Registrations)
        {
            Register(registration.Family, registration.Regular, registration.Bold, registration.Italic, registration.BoldItalic);
        }

        AddBundledFont();
        Scan();
    }

    /// <summary>フォントファイルを明示的に登録します。</summary>
    public void Register(string family, string regular, string? bold = null, string? italic = null, string? boldItalic = null)
    {
        if (string.IsNullOrWhiteSpace(family)) throw new ArgumentException("Font family is required.", nameof(family));
        Add(family, 400, false, regular, 0);
        Add(family, 700, false, bold, 0);
        Add(family, 400, true, italic, 0);
        Add(family, 700, true, boldItalic, 0);
        _cache.Clear();
    }

    /// <inheritdoc />
    public ResolvedFont Resolve(FontRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Family)) throw new ArgumentException("Font family is required.", nameof(request));
        if (_cache.TryGetValue(request, out var cached)) return cached;

        var requestedFamilies = new[] { request.Family }.Concat(_options.FallbackFamilies)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = _faces        .OrderBy(x => x.SourcePriority)
            .ThenBy(x => x.SortKey, StringComparer.Ordinal);

        foreach (var family in requestedFamilies)
        {
            var matching = candidates.Where(x => string.Equals(x.Family, family, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length > 0) return _cache[request] = Select(matching, request);
        }

        throw new InvalidOperationException(
            $"No usable font was found. Requested: \"{request.Family}\" Fallbacks: {string.Join(", ", _options.FallbackFamilies)}");
    }

    /// <inheritdoc />
    public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var elements = StringInfo.GetTextElementEnumerator(text);
        var runs = new List<TextRun>();
        while (elements.MoveNext())
        {
            var element = (string)elements.Current!;
            var font = ResolveForTextElement(element, request);
            if (runs.Count > 0 && runs[^1].Font.FaceId == font.FaceId)
            {
                runs[^1] = runs[^1] with { Text = runs[^1].Text + element };
            }
            else
            {
                runs.Add(new(element, font));
            }
        }

        return runs;
    }

    private ResolvedFont ResolveForTextElement(string element, FontRequest request)
    {
        var primary = Resolve(request);
        if (Supports(primary, element)) return primary;

        foreach (var family in _options.FallbackFamilies.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var fallback = Resolve(new FontRequest(family, request.Weight, request.Italic));
            if (Supports(fallback, element)) return fallback;
        }

        return primary;
    }

    private static bool Supports(ResolvedFont font, string text)
    {
        using Stream stream = font.FontData is null
            ? File.OpenRead(font.FilePath)
            : new MemoryStream(font.FontData, writable: false);
        using var typeface = SKTypeface.FromStream(stream);
        return typeface is not null && typeface.GetGlyphs(text).All(glyph => glyph != 0);
    }

    private static ResolvedFont Select(IEnumerable<FontFace> candidates, FontRequest request)
    {
        var selected = candidates
            .OrderBy(x => x.Italic == request.Italic ? 0 : 1)
            .ThenBy(x => Math.Abs(x.Weight - request.Weight))
            .ThenBy(x => x.SortKey, StringComparer.Ordinal)
            .First();
        return new(selected.Family, selected.Weight, selected.Italic, selected.FilePath)
        {
            FaceId = selected.FaceId,
            FontData = selected.Data,
            FontStyleApproximated = selected.Weight != request.Weight || selected.Italic != request.Italic,
        };
    }

    private void AddBundledFont()
    {
        if (BundledJapaneseFont.Data is not { } data) return;
        using var stream = new MemoryStream(data, writable: false);
        using var typeface = SKTypeface.FromStream(stream);
        if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName)) return;
        Add(typeface.FamilyName, typeface.FontStyle.Weight, typeface.FontStyle.Slant != SKFontStyleSlant.Upright,
            BundledJapaneseFont.FaceName, data, true, 1);
    }

    private void Add(string family, int weight, bool italic, string? path, int sourcePriority)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Add(family, weight, italic, Path.GetFullPath(path), null, false, sourcePriority);
    }

    private void Add(string family, int weight, bool italic, string path, byte[]? data, bool bundled, int sourcePriority)
    {
        var bytes = data ?? File.ReadAllBytes(path);
        using var hasher = SHA256.Create();
        var id = BitConverter.ToString(hasher.ComputeHash(bytes)).Replace("-", string.Empty, StringComparison.Ordinal);
        var candidate = new FontFace(family, weight, italic, path, bytes, id, bundled, sourcePriority);
        if (_faces.Any(x => x.Family.Equals(candidate.Family, StringComparison.OrdinalIgnoreCase) &&
            x.Weight == candidate.Weight && x.Italic == candidate.Italic &&
            string.Equals(x.FilePath, candidate.FilePath, StringComparison.OrdinalIgnoreCase))) return;
        _faces.Add(candidate);
    }

    private void Scan()
    {
        var directories = _options.FontDirectories;
        if (_options.AllowSystemFonts)
        {
            directories = directories.Concat(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                ? [Environment.GetFolderPath(Environment.SpecialFolder.Fonts)]
                : ["/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")]).ToArray();
        }

        var configuredDirectories = new HashSet<string>(
            _options.FontDirectories.Where(Directory.Exists).Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(x => x.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    using var typeface = SKTypeface.FromFile(file);
                    if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName)) continue;
                    var style = typeface.FontStyle;
                    Add(typeface.FamilyName, style.Weight, style.Slant != SKFontStyleSlant.Upright, Path.GetFullPath(file),
                        null, false, configuredDirectories.Contains(directory) ? 0 : 2);
                }
                catch (IOException)
                {
                    // A file can disappear while an external directory is being scanned.
                }
                catch (UnauthorizedAccessException)
                {
                    // Continue scanning other files; callers can avoid inaccessible roots with FontDirectories.
                }
            }
        }
    }

    private sealed record FontFace(string Family, int Weight, bool Italic, string FilePath, byte[] Data, string FaceId, bool IsBundled, int SourcePriority)
    {
        public string SortKey => $"{Family}\0{Weight:D4}\0{Italic}\0{FilePath}";
    }
}
