using ExcelRenderer.Core.Fonts.TrueType;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Fonts;

/// <summary>Owns validated, immutable explicit font snapshots for one conversion.</summary>
internal sealed class SingleFontContext : IDisposable
{
    /// <summary>The legacy single-font family used by reader, layout, and renderer.</summary>
    internal const string Family = "CoreSingleFont";
    private readonly List<SkiaFontFace> faces = [];
    private readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Family, int Classification), SkiaFontFace> resolved = new();
    private readonly SkiaFontFace? sansSerif;
    private readonly SkiaFontFace? serif;

    /// <summary>Initializes a new instance of the <see cref="SingleFontContext"/> class.</summary>
    /// <param name="path">The static TrueType file.</param>
    /// <param name="collectionIndex">The zero-based collection face index.</param>
    /// <param name="token">The conversion cancellation token.</param>
    internal SingleFontContext(string path, int collectionIndex = 0, CancellationToken token = default)
    {
        var bytes = ReadFace(path, collectionIndex, token);
        ConversionMetrics.Report("fontSnapshotRead", 1);
        Face = new SkiaFontFace(bytes);
        faces.Add(Face);
    }

    /// <summary>Initializes a new instance of the <see cref="SingleFontContext"/> class.</summary>
    /// <param name="options">Explicit files and family selection rules.</param>
    /// <param name="token">The conversion token.</param>
    internal SingleFontContext(FontOptions options, CancellationToken token = default)
    {
        IsMultiple = true;
        if (options.Files is null || options.Files.Count == 0 || options.FamilyAliases is null)
        {
            throw new ArgumentException("Fonts requires at least one file and a nonnull FamilyAliases collection.", nameof(options));
        }

        try
        {
            foreach (var path in options.Files.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentException("Font paths must not be empty.", nameof(options));
                }

                var bytes = ReadFace(path, 0, token);
                ConversionMetrics.Report("fontSnapshotRead", 1);
                faces.Add(new SkiaFontFace(bytes));
            }

            Face = Configured(options.DefaultFamily) ?? faces[0];
            sansSerif = Configured(options.SansSerifFallback);
            serif = Configured(options.SerifFallback);
            foreach (var pair in options.FamilyAliases)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    throw new ArgumentException("Font aliases require nonempty source and target families.", nameof(options));
                }

                _ = Configured(pair.Value);
                aliases.Add(pair.Key, pair.Value);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets a value indicating whether source cell families select explicit faces.</summary>
    internal bool IsMultiple { get; }

    /// <summary>Gets the immutable font snapshot shared by measurement, subset and drawing.</summary>
    internal SkiaFontFace Face { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var face in faces)
        {
            face.Dispose();
        }

        faces.Clear();
        resolved.Clear();
    }

    /// <summary>Finds an original physical family among supplied fonts, without aliases.</summary>
    /// <param name="family">The source family.</param>
    /// <returns>The borrowed exact face, or null.</returns>
    internal SkiaFontFace? FindExact(string family) => faces.FirstOrDefault(face => ColumnFontMetrics.MatchesFamily(face.Typeface, family));

    /// <summary>Selects one physical face for the whole cell; missing glyphs do not switch fonts.</summary>
    /// <param name="family">The cell family or internally finalized face identity.</param>
    /// <param name="classification">Saved Roman (1) or Swiss (2) classification.</param>
    /// <returns>The conversion-owned physical face.</returns>
    internal SkiaFontFace Resolve(string family, int classification = 0)
    {
        if (!IsMultiple)
        {
            return Face;
        }

        var key = (family, classification);
        if (!resolved.TryGetValue(key, out var face))
        {
            face = faces.FirstOrDefault(item => item.Identity == family) ?? FindExact(family);
            if (face is null && aliases.TryGetValue(family, out var target))
            {
                face = FindExact(target);
            }

            face ??= (classification == 1 ? serif : classification == 2 ? sansSerif : null) ?? Face;
            resolved.Add(key, face);
        }

        return face;
    }

    /// <summary>Gets line metrics from the same snapshot used by drawing.</summary>
    /// <param name="size">The point size.</param>
    /// <returns>The ascent, descent and leading.</returns>
    internal (double Ascent, double Descent, double Leading) Metrics(double size) => Face.Metrics(size);

    /// <summary>Measures Normal-style maximum digit width at 96 DPI.</summary>
    /// <param name="size">The Normal font point size.</param>
    /// <returns>The rounded maximum width in pixels.</returns>
    /// <param name="libreOfficeCompatible">Whether to round digit advances to twips instead of integer pixels.</param>
    internal double MaximumDigitWidth(double size, bool libreOfficeCompatible = false)
    {
        return ColumnFontMetrics.Measure(Face.Typeface, size, libreOfficeCompatible);
    }

    private static byte[] ReadFace(string path, int collectionIndex, CancellationToken token)
    {
        try
        {
            var bytes = FontFaceExtractor.Extract(File.ReadAllBytes(path), collectionIndex, token);
            FontCapabilities.ValidatePdf(new SfntReader(bytes, token: token), token);
            return bytes;
        }
        catch (NotSupportedException error)
        {
            // Core has historically reported unsupported single-font inputs as invalid data.
            throw new InvalidDataException($"Font '{path}', face {collectionIndex}: {error.Message}", error);
        }
    }

    private SkiaFontFace? Configured(string? family) => family is null ? null : FindExact(family)
        ?? throw new ArgumentException($"Configured font family '{family}' is not present in Fonts.Files.");
}
