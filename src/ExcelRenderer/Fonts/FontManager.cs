using System.Globalization;
using System.Security.Cryptography;
using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>登録済み、追加ディレクトリ、および許可されたシステムフォントを決定的に解決します。</summary>
public sealed class FontManager : IFontManager
{
    private readonly Dictionary<(string Face, string Text), bool> _support = new();
    private readonly Dictionary<(FontRequest Request, string Text), IReadOnlyList<TextRun>> _runs = new();
    private readonly FontOptions _options;

    private readonly List<FontFace> _faces = [];
    private readonly List<FontFace> _externalFaces = [];
    private readonly Dictionary<FontRequest, ResolvedFont> _cache = new();
    private readonly Dictionary<(int RegistrationId, int Weight, bool Italic), ResolvedFont> _resolvedFaces = new();
    private readonly Dictionary<(string FaceId, int Base, int Selector), OpenTypeVariationSequences.Resolution?> _ivsSupport = new();

    /// <summary>Initializes a new instance of the <see cref="FontManager"/> class. 指定された設定でフォントマネージャーを初期化します。</summary>
    /// <param name="options">登録フォント、追加検索フォルダー、フォールバック名を含む設定です。省略時は既定設定を使用します。</param>
    public FontManager(FontOptions? options = null)
    {
        _options = options ?? new();
        foreach (var registration in _options.Registrations)
        {
            Register(registration.Family, registration.Regular, registration.Bold, registration.Italic, registration.BoldItalic);
        }

        for (var i = 0; i < _options.FontFiles.Count; i++)
        {
            AddExternalFile(_options.FontFiles[i], i);
        }

        if (_options.UseFontPack)
        {
            AddFontPack();
        }

        Scan();
    }

    /// <summary>Gets the registration generation used by layout caches.</summary>
    internal int RegistrationGeneration { get; private set; }

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
        _runs.Clear();
        RegistrationGeneration++;
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

        var cacheKey = (request, text);
        if (_runs.TryGetValue(cacheKey, out var cachedRuns))
        {
            return cachedRuns;
        }

        var runs = new List<TextRun>();
        foreach (var (element, start, baseScalar, selector) in EnumerateElements(text))
        {
            var replaceIvs = selector is not null && _options.ReplaceIvsWithBaseCharacter;
            var renderedText = replaceIvs ? char.ConvertFromUtf32(baseScalar) : element;
            ResolvedFont font;
            bool missing;
            bool missingPrivateUse;
            OpenTypeVariationSequences.Resolution? glyph;
            ushort? colorEmojiGlyph = null;
            if (selector is null || replaceIvs)
            {
                (font, colorEmojiGlyph, missingPrivateUse) = ResolveForTextElement(renderedText, baseScalar, request);
                missing = false;
                glyph = null;
            }
            else
            {
                (font, missing, glyph) = ResolveIvs(baseScalar, selector.Value, request);
                missingPrivateUse = false;
            }

            renderedText = missing || missingPrivateUse ? "\uFFFD" : renderedText;
            if (runs.Count > 0 && runs[^1].Font.FaceId == font.FaceId &&
                glyph is null && colorEmojiGlyph is null && runs[^1].GlyphId is null &&
                runs[^1].ColorEmojiGlyphId is null && runs[^1].MissingIvsGlyph == missing &&
                runs[^1].MissingPrivateUseGlyph == missingPrivateUse &&
                runs[^1].Utf16Start + runs[^1].SourceText.Length == start)
            {
                runs[^1] = runs[^1] with { Text = runs[^1].Text + renderedText, SourceText = runs[^1].SourceText + element };
            }
            else
            {
                runs.Add(new(renderedText, font)
                {
                    Utf16Start = start,
                    SourceText = element,
                    MissingIvsGlyph = missing,
                    MissingPrivateUseGlyph = missingPrivateUse,
                    GlyphId = glyph?.GlyphId,
                    ColorEmojiGlyphId = colorEmojiGlyph,
                    IsDefaultVariationGlyph = glyph?.IsDefault ?? false,
                });
            }
        }

        var result = runs.AsReadOnly();
        if (text.Length <= 2048)
        {
            if (_runs.Count >= 512)
            {
                _runs.Clear();
            }

            _runs[cacheKey] = result;
        }

        return result;
    }

    private static IEnumerable<(string Text, int Start, int BaseScalar, int? Selector)> EnumerateElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current!;
            var start = enumerator.ElementIndex;
            var scalars = UnicodeScalars(element).ToArray();
            var selector = scalars.Length == 2 && IsVariationSelector(scalars[1]) && IsIdeographicBase(scalars[0])
                ? scalars[1]
                : (int?)null;
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

    private static bool IsIdeographicBase(int scalar) => scalar is
        >= 0x3400 and <= 0x4DBF or
        >= 0x4E00 and <= 0x9FFF or
        >= 0xF900 and <= 0xFAFF or
        >= 0x20000 and <= 0x2FA1F or
        >= 0x30000 and <= 0x323AF;

    private static bool SupportsUncached(ResolvedFont font, string text)
    {
        using Stream stream = font.FontData is null
            ? File.OpenRead(font.FilePath)
            : new MemoryStream(font.FontData, writable: false);
        using var typeface = SKTypeface.FromStream(stream);
        return typeface is not null && typeface.GetGlyphs(text).All(glyph => glyph != 0);
    }

    private bool Supports(ResolvedFont font, string text)
    {
        var key = (font.FaceId, text);
        ExcelRenderer.Rendering.ConversionMetrics.Report("supportChecks", 1);
        if (_support.TryGetValue(key, out var supported))
        {
            ExcelRenderer.Rendering.ConversionMetrics.Report("supportCacheHits", 1);
            return supported;
        }

        if (ConversionFontResources.Current is { } resources)
        {
            supported = resources.GetTypeface(font).GetGlyphs(text).All(glyph => glyph != 0);
        }
        else
        {
            supported = SupportsUncached(font, text);
        }

        if (_support.Count >= 8192)
        {
            _support.Clear();
        }

        if (text.Length <= 256)
        {
            _support[key] = supported;
        }

        return supported;
    }

    private ResolvedFont Select(IEnumerable<FontFace> candidates, FontRequest request)
    {
        var selected = candidates
            .OrderBy(x => x.Italic == request.Italic ? 0 : 1)
            .ThenBy(x => Math.Abs(x.Weight - request.Weight))
            .ThenBy(x => x.SortKey, StringComparer.Ordinal)
            .First();

        // FaceId identifies the bytes and is deliberately shared by aliases.  A resolved
        // result also describes the selected registration (family and registered style),
        // so it must not be shared by registrations which happen to contain those bytes.
        var key = (selected.RegistrationId, request.Weight, request.Italic);
        if (_resolvedFaces.TryGetValue(key, out var cached))
        {
            return cached;
        }

        return _resolvedFaces[key] = new(selected.Family, selected.Weight, selected.Italic, selected.FilePath)
        {
            FaceId = selected.FaceId,
            FontData = selected.Data,
            FontStyleApproximated = selected.Weight != request.Weight || selected.Italic != request.Italic,
        };
    }

    private (ResolvedFont Font, bool Missing, OpenTypeVariationSequences.Resolution? Glyph) ResolveIvs(int baseScalar, int selector, FontRequest request)
    {
        // IVS selection is intentionally independent of ordinary family fallback.
        foreach (var face in _faces
            .Where(x => x.IsBundled && x.Family != "Noto Color Emoji" &&
                (x.IvsFontStyle == _options.IvsFontStyle ||
                    (_options.IvsFontStyle == IvsFontStyle.Gothic && x.IvsFontStyle == IvsFontStyle.Mincho)))
            .OrderBy(x => x.IvsPriority))
        {
            var font = Select([face], request);
            var key = (font.FaceId, baseScalar, selector);
            if (!_ivsSupport.TryGetValue(key, out var resolution))
            {
                resolution = OpenTypeVariationSequences.TryResolve(face.Data, baseScalar, selector, out var found) ? found : null;
                _ivsSupport[key] = resolution;
            }

            if (resolution is not null)
            {
                return (font, false, resolution);
            }
        }

        return (Resolve(request), true, null);
    }

    private (ResolvedFont Font, ushort? ColorEmojiGlyph, bool MissingPrivateUse) ResolveForTextElement(string element, int baseScalar, FontRequest request)
    {
        var primary = Resolve(request);
        var scalars = UnicodeScalars(element).ToArray();
        if (scalars.Length == 2 && scalars[1] == 0xFE0F && TryColorEmoji(scalars[0], request) is { } requestedEmoji)
        {
            return (requestedEmoji.Font, requestedEmoji.Glyph, false);
        }

        if (Supports(primary, element))
        {
            return (primary, null, false);
        }

        if (baseScalar is >= 0xE000 and <= 0xF8FF && _externalFaces.Count > 0)
        {
            foreach (var face in _externalFaces)
            {
                var external = Select([face], request);
                if (Supports(external, element))
                {
                    return (external, null, false);
                }
            }
        }

        // A single emoji scalar, optionally followed by VS16, is rendered with the
        // bitmap glyph from the optional color font. Complex ZWJ sequences need shaping.
        if (scalars.Length is 1 or 2 && (scalars.Length == 1 || scalars[1] == 0xFE0F) &&
            TryColorEmoji(scalars[0], request) is { } emoji)
        {
            return (emoji.Font, emoji.Glyph, false);
        }

        foreach (var family in _options.FallbackFamilies.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var fallback = Resolve(new FontRequest(family, request.Weight, request.Italic));
            if (Supports(fallback, element))
            {
                return (fallback, null, false);
            }
        }

        return (primary, null, baseScalar is >= 0xE000 and <= 0xF8FF && _externalFaces.Count > 0);
    }

    private void AddExternalFile(string path, int order)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An explicitly configured font file path cannot be empty.", nameof(_options.FontFiles));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ArgumentException($"Font file was not found: {fullPath}", nameof(_options.FontFiles));
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"Font file could not be read: {fullPath}", nameof(_options.FontFiles), error);
        }

        using var stream = new MemoryStream(bytes, writable: false);
        using var typeface = SKTypeface.FromStream(stream);
        if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName))
        {
            throw new ArgumentException($"File is not a supported TrueType/OpenType font: {fullPath}", nameof(_options.FontFiles));
        }

        var style = typeface.FontStyle;
        var face = Add(
            typeface.FamilyName,
            style.Weight,
            style.Slant != SKFontStyleSlant.Upright,
            fullPath,
            bytes,
            false,
            0,
            int.MaxValue,
            null,
            order);
        if (!_externalFaces.Any(x => x.FaceId == face.FaceId))
        {
            _externalFaces.Add(face);
        }
    }

    private (ResolvedFont Font, ushort Glyph)? TryColorEmoji(int scalar, FontRequest request)
    {
        if (scalar is not (>= 0x1F300 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF) ||
            _faces.FirstOrDefault(x => x.IsBundled && x.Family == "Noto Color Emoji") is not { } emoji)
        {
            return null;
        }

        var resolved = Select([emoji], request);
        using var stream = new MemoryStream(emoji.Data, writable: false);
        using var ownedTypeface = ConversionFontResources.Current is null ? SKTypeface.FromStream(stream) : null;
        var typeface = ConversionFontResources.Current?.GetTypeface(resolved) ?? ownedTypeface;
        using var font = typeface is null ? null : new SKFont(typeface);
        var glyph = font?.GetGlyph(scalar) ?? 0;
        return glyph == 0 ? null : (Select([emoji], request), glyph);
    }

    private void AddFontPack()
    {
        foreach (var resource in OptionalFontPack.Fonts)
        {
            var data = resource.Data;
            using var stream = new MemoryStream(data, writable: false);
            using var typeface = SKTypeface.FromStream(stream);
            if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName))
            {
                continue;
            }

            var style = resource.Name == "ipamjm.ttf" ? IvsFontStyle.Mincho : (IvsFontStyle?)null;
            var ivsPriority = style is null ? 1 : 0;
            Add(
                typeface.FamilyName,
                typeface.FontStyle.Weight,
                typeface.FontStyle.Slant != SKFontStyleSlant.Upright,
                resource.Name,
                data,
                true,
                1,
                ivsPriority,
                style);
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

    private FontFace Add(string family, int weight, bool italic, string path, byte[]? data, bool bundled, int sourcePriority, int ivsPriority, IvsFontStyle? ivsFontStyle, int explicitOrder = int.MaxValue)
    {
        var bytes = data ?? File.ReadAllBytes(path);
        using var hasher = SHA256.Create();
        var id = BitConverter.ToString(hasher.ComputeHash(bytes)).Replace("-", string.Empty, StringComparison.Ordinal);
        var candidate = new FontFace(
            _faces.Count,
            family,
            weight,
            italic,
            path,
            bytes,
            id,
            bundled,
            sourcePriority,
            ivsPriority,
            ivsFontStyle,
            explicitOrder);
        if (_faces.Any(x => x.Family.Equals(candidate.Family, StringComparison.OrdinalIgnoreCase) &&
            x.Weight == candidate.Weight && x.Italic == candidate.Italic &&
            string.Equals(x.FilePath, candidate.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            return _faces.First(x => x.Family.Equals(candidate.Family, StringComparison.OrdinalIgnoreCase) &&
                x.Weight == candidate.Weight && x.Italic == candidate.Italic &&
                string.Equals(x.FilePath, candidate.FilePath, StringComparison.OrdinalIgnoreCase));
        }

        _faces.Add(candidate);
        return candidate;
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

    private sealed record FontFace(int RegistrationId, string Family, int Weight, bool Italic, string FilePath, byte[] Data, string FaceId, bool IsBundled, int SourcePriority, int IvsPriority, IvsFontStyle? IvsFontStyle, int ExplicitOrder = int.MaxValue)
    {
        public string SortKey => $"{ExplicitOrder:D8}\0{Family}\0{Weight:D4}\0{Italic}\0{FilePath}";
    }
}
