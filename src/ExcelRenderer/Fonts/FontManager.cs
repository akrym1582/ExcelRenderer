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
    private readonly Dictionary<(string FaceId, int Base, int Selector), bool> _ivsSupport = new();

    /// <summary>Initializes a new instance of the <see cref="FontManager"/> class. 指定された設定でフォントマネージャーを初期化します。</summary>
    /// <param name="options">登録フォント、追加検索フォルダー、フォールバック名を含む設定です。省略時は既定設定を使用します。</param>
    public FontManager(FontOptions? options = null)
    {
        _options = options ?? new();
        foreach (var registration in _options.Registrations)
        {
            Register(registration.Family, registration.Regular, registration.Bold, registration.Italic, registration.BoldItalic);
        }

        AddBundledFonts();
        Scan();
    }

    /// <summary>フォントファイルを明示的に登録し、以後の解決対象に追加します。</summary>
    /// <param name="family">登録するフォントファミリー名です。</param>
    /// <param name="regular">通常体のフォントファイルパスです。</param>
    /// <param name="bold">太字体のフォントファイルパスです。指定しない場合は通常体が候補になります。</param>
    /// <param name="italic">斜体のフォントファイルパスです。指定しない場合は通常体が候補になります。</param>
    /// <param name="boldItalic">太字斜体のフォントファイルパスです。指定しない場合は通常体が候補になります。</param>
    public void Register(string family, string regular, string? bold = null, string? italic = null, string? boldItalic = null)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("Font family is required.", nameof(family));
        }

        Add(family, 400, false, regular, 0);
        Add(family, 700, false, bold, 0);
        Add(family, 400, true, italic, 0);
        Add(family, 700, true, boldItalic, 0);
        _cache.Clear();
    }

    /// <summary>要求されたファミリー名と書体属性に最も近い使用可能なフォントを解決します。</summary>
    /// <param name="request">必要なフォントファミリー、ウェイト、および斜体を指定する要求です。</param>
    /// <returns>描画に使用するフォントファイルと選択結果を表すフォント情報を返します。</returns>
    public ResolvedFont Resolve(FontRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Family))
        {
            throw new ArgumentException("Font family is required.", nameof(request));
        }

        if (_cache.TryGetValue(request, out var cached))
        {
            return cached;
        }

        var requestedFamilies = new[] { request.Family }.Concat(_options.FallbackFamilies)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = _faces
            .OrderBy(x => x.SourcePriority)
            .ThenBy(x => x.SortKey, StringComparer.Ordinal);

        foreach (var family in requestedFamilies)
        {
            var matching = candidates.Where(x => string.Equals(x.Family, family, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length > 0)
            {
                return _cache[request] = Select(matching, request);
            }
        }

        throw new InvalidOperationException(
            $"No usable font was found. Requested: \"{request.Family}\" Fallbacks: {string.Join(", ", _options.FallbackFamilies)}");
    }

    /// <summary>文字列を Unicode の書記素クラスタごとに分割し、各文字を表示できるフォントの実行列へ変換します。</summary>
    /// <param name="text">フォントを解決する文字列です。</param>
    /// <param name="request">最初に試すフォントファミリーと書体属性を指定する要求です。</param>
    /// <returns>連続して同じフォントを使用できる文字をまとめたフォント実行列を返します。</returns>
    public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var runs = new List<TextRun>();
        foreach (var (element, start, baseScalar, selector) in EnumerateElements(text))
        {
            var (font, missing) = selector is null
                ? (ResolveForTextElement(element, request), false)
                : ResolveIvs(baseScalar, selector.Value, request);
            var renderedText = missing ? "\uFFFD" : element;
            if (runs.Count > 0 && runs[^1].Font.FaceId == font.FaceId &&
                runs[^1].MissingIvsGlyph == missing && runs[^1].Utf16Start + runs[^1].SourceText.Length == start)
            {
                runs[^1] = runs[^1] with { Text = runs[^1].Text + renderedText, SourceText = runs[^1].SourceText + element };
            }
            else
            {
                runs.Add(new(renderedText, font) { Utf16Start = start, SourceText = element, MissingIvsGlyph = missing });
            }
        }

        return runs;
    }

    private static IEnumerable<(string Text, int Start, int BaseScalar, int? Selector)> EnumerateElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current!;
            var start = enumerator.ElementIndex;
            var scalars = UnicodeScalars(element).ToArray();
            var selector = scalars.Length == 2 && IsVariationSelector(scalars[1]) ? scalars[1] : (int?)null;
            yield return (element, start, scalars[0], selector);
        }
    }

    private static IEnumerable<int> UnicodeScalars(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            yield return char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])
                ? char.ConvertToUtf32(value[i], value[++i]) : value[i];
        }
    }

    private static bool IsVariationSelector(int scalar) => scalar is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF;

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

    private (ResolvedFont Font, bool Missing) ResolveIvs(int baseScalar, int selector, FontRequest request)
    {
        // IVS selection is intentionally independent of ordinary family fallback: bundled Noto first, then IPAmj.
        foreach (var face in _faces
            .Where(x => x.IsBundled && (x.IvsFontStyle == _options.IvsFontStyle || x.IvsFontStyle is null))
            .OrderBy(x => x.IvsPriority))
        {
            var font = Select([face], request);
            var key = (font.FaceId, baseScalar, selector);
            if (!_ivsSupport.TryGetValue(key, out var supported))
            {
                supported = OpenTypeVariationSequences.Supports(face.Data, baseScalar, selector);
                _ivsSupport[key] = supported;
            }

            if (supported)
            {
                return (font, false);
            }
        }

        return (Resolve(request), true);
    }

    private ResolvedFont ResolveForTextElement(string element, FontRequest request)
    {
        var primary = Resolve(request);
        if (Supports(primary, element))
        {
            return primary;
        }

        foreach (var family in _options.FallbackFamilies.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var fallback = Resolve(new FontRequest(family, request.Weight, request.Italic));
            if (Supports(fallback, element))
            {
                return fallback;
            }
        }

        return primary;
    }

    private void AddBundledFonts()
    {
        if (BundledJapaneseFont.Data is { } data)
        {
            using var stream = new MemoryStream(data, writable: false);
            using var typeface = SKTypeface.FromStream(stream);
            if (typeface is not null && !string.IsNullOrWhiteSpace(typeface.FamilyName))
            {
                Add(
                    typeface.FamilyName,
                    typeface.FontStyle.Weight,
                    typeface.FontStyle.Slant != SKFontStyleSlant.Upright,
                    BundledJapaneseFont.FaceName,
                    data,
                    true,
                    1,
                    0,
                    IvsFontStyle.Gothic);
                if (!string.Equals(typeface.FamilyName, "Noto Sans JP", StringComparison.OrdinalIgnoreCase))
                {
                    Add(
                        "Noto Sans JP",
                        typeface.FontStyle.Weight,
                        typeface.FontStyle.Slant != SKFontStyleSlant.Upright,
                        BundledJapaneseFont.FaceName,
                        data,
                        true,
                        1,
                        0,
                        IvsFontStyle.Gothic);
                }
            }
        }

        if (BundledJapaneseSerifFont.Data is { } serifData && BundledJapaneseSerifFont.FamilyName is { } serifFamily)
        {
            Add(serifFamily, 400, false, BundledJapaneseSerifFont.FaceName, serifData, true, 1, 0, IvsFontStyle.Mincho);
        }

        if (BundledIvsFont.Data is { } ivsData && BundledIvsFont.FamilyName is { } ivsFamily)
        {
            Add(ivsFamily, 400, false, BundledIvsFont.FaceName, ivsData, true, 1, 1, null);
        }
    }

    private void Add(string family, int weight, bool italic, string? path, int sourcePriority)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        Add(family, weight, italic, Path.GetFullPath(path), null, false, sourcePriority, int.MaxValue, null);
    }

    private void Add(string family, int weight, bool italic, string path, byte[]? data, bool bundled, int sourcePriority, int ivsPriority, IvsFontStyle? ivsFontStyle)
    {
        var bytes = data ?? File.ReadAllBytes(path);
        using var hasher = SHA256.Create();
        var id = BitConverter.ToString(hasher.ComputeHash(bytes)).Replace("-", string.Empty, StringComparison.Ordinal);
        var candidate = new FontFace(family, weight, italic, path, bytes, id, bundled, sourcePriority, ivsPriority, ivsFontStyle);
        if (_faces.Any(x => x.Family.Equals(candidate.Family, StringComparison.OrdinalIgnoreCase) &&
            x.Weight == candidate.Weight && x.Italic == candidate.Italic &&
            string.Equals(x.FilePath, candidate.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

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
                    if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName))
                    {
                        continue;
                    }

                    var style = typeface.FontStyle;
                    Add(
                        typeface.FamilyName,
                        style.Weight,
                        style.Slant != SKFontStyleSlant.Upright,
                        Path.GetFullPath(file),
                        null,
                        false,
                        configuredDirectories.Contains(directory) ? 0 : 2,
                        int.MaxValue,
                        null);
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

    private sealed record FontFace(string Family, int Weight, bool Italic, string FilePath, byte[] Data, string FaceId, bool IsBundled, int SourcePriority, int IvsPriority, IvsFontStyle? IvsFontStyle)
    {
        public string SortKey => $"{Family}\0{Weight:D4}\0{Italic}\0{FilePath}";
    }
}
