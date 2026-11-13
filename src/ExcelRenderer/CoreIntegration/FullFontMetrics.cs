using ExcelRenderer.Core.Excel;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using SkiaSharp;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Resolves source layout metrics independently of rendering substitutions.</summary>
internal sealed class FullFontMetrics : ICoreFontMetrics
{
    private readonly DiagnosticCollector? diagnostics;
    private readonly ColumnWidthMetricsOptions options;
    private readonly MetricCache cache;
    private IFontManager? fontManager;

    /// <summary>Initializes a new instance of the <see cref="FullFontMetrics"/> class.</summary>
    /// <param name="fontManager">The optional drawing manager, with bounded exact discovery.</param>
    /// <param name="diagnostics">The conversion collector.</param>
    /// <param name="readOptions">The source metric policy and optional conversion cache.</param>
    internal FullFontMetrics(IFontManager? fontManager, DiagnosticCollector? diagnostics, ExcelReadOptions? readOptions = null)
    {
        this.fontManager = fontManager;
        this.diagnostics = diagnostics;
        options = readOptions?.ColumnWidthMetrics ?? new();
        cache = readOptions?.ColumnWidthMetricsCache ?? new();
    }

    /// <inheritdoc/>
    public NormalFontMetadata DefaultFont => new("Calibri", 11);

    /// <inheritdoc/>
    public bool UseLibreOfficeColumnWidths => options.Mode == ColumnWidthMetricsMode.LibreOfficeCompatible;

    /// <inheritdoc/>
    public double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName)
    {
        var normal = normalFont ?? DefaultFont;
        var key = (normalFont, options, (fontManager as FontManager)?.RegistrationGeneration ?? 0);
        if (!cache.Values.TryGetValue(key, out var decision))
        {
            decision = normalFont is null && options.MaximumDigitWidthPixels96Dpi is null && options.SourceFontFile is null
                ? options.Mode == ColumnWidthMetricsMode.RenderingFontLegacy
                    ? new(7, "legacy", "7px compatibility metric (Normal metadata unavailable)", true)
                    : ResolveFallback(normal)
                : Resolve(normal);
            cache.Values.Add(key, decision);
        }

        var fallback = decision.IsFallback;
        var message = $"Normal '{normalFont?.Family ?? "unknown"}', {normal.Size}pt, weight {normal.Weight}, italic {normal.Italic}; measurement '{decision.Font}', MDW {decision.Width}px at 96 DPI; source={decision.Source}.";
        if (fallback)
        {
            message += " Exact Excel layout cannot be guaranteed; specify SourceFontFile or MaximumDigitWidthPixels96Dpi to supply original metrics.";
        }

        diagnostics?.Add(new(
            fallback ? "MaximumDigitWidthFallback" : "MaximumDigitWidthResolved",
            fallback ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
            DiagnosticStage.Read,
            message,
            sheetName));
        return decision.Width;
    }

    private MetricDecision Measure(ResolvedFont resolved, NormalFontMetadata normal, string source)
    {
        using var ownedResources = ConversionFontResources.Current is null ? new ConversionFontResources() : null;
        var typeface = ConversionFontResources.Current!.GetTypeface(resolved);
        var width = Core.Fonts.ColumnFontMetrics.Measure(typeface, normal.Size, UseLibreOfficeColumnWidths);
        var style = typeface.FontStyle;
        var italic = style.Slant != SKFontStyleSlant.Upright;
        var approximated = style.Weight != normal.Weight || italic != normal.Italic;
        return new(width, source, $"{typeface.FamilyName}, weight {style.Weight}, italic {italic}, approximated {approximated}, face {resolved.FontCollectionIndex}, {resolved.FaceId}");
    }

    private MetricDecision Resolve(NormalFontMetadata normal)
    {
        if (options.MaximumDigitWidthPixels96Dpi is { } width)
        {
            return new(width, "override", "explicit MDW");
        }

        if (options.SourceFontFile is not null)
        {
            var bytes = options.LoadSourceFont();
            var resolved = new ResolvedFont(normal.Family, normal.Weight, normal.Italic, options.SourceFontFile)
            {
                FontData = bytes,
                FontCollectionIndex = options.SourceFontCollectionIndex ?? 0,
                StandaloneData = bytes,
                StandaloneCollectionIndex = options.SourceFontCollectionIndex ?? 0,
                FaceId = $"{options.SourceFontFile}#{options.SourceFontCollectionIndex ?? 0}",
            };
            return Measure(resolved, normal, "source-file");
        }

        try
        {
            if (options.Mode is ColumnWidthMetricsMode.SourceFont or ColumnWidthMetricsMode.LibreOfficeCompatible)
            {
                fontManager ??= new FontManager();
            }

            var request = new FontRequest(normal.Family, normal.Weight, normal.Italic);
            if (options.Mode == ColumnWidthMetricsMode.RenderingFontLegacy && fontManager is not null)
            {
                // Historical MDW resolution requested the regular drawing family.
                return Measure(fontManager.Resolve(new(normal.Family)), normal, "legacy");
            }

            if (UseLibreOfficeColumnWidths && fontManager is not null)
            {
                var substitute = fontManager.Resolve(request);
                var matches = FontManager.MatchesSourceFamily(substitute, normal.Family);
                return !matches && options.FallbackMaximumDigitWidthPixels96Dpi is { } fallbackWidth
                    ? new(fallbackWidth, "fallback-override", "explicit fallback MDW", true)
                    : Measure(substitute, normal, "libreoffice-font") with { IsFallback = !matches };
            }

            var exact = fontManager is FontManager builtIn ? builtIn.ResolveExact(request) : fontManager?.Resolve(request);
            if (exact is not null && FontManager.MatchesSourceFamily(exact, normal.Family))
            {
                return Measure(exact, normal, "exact-font");
            }
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            // Implicit discovery is best effort; explicit files are validated above.
        }

        if (options.Mode == ColumnWidthMetricsMode.RenderingFontLegacy)
        {
            return new(7, "legacy", "7px compatibility metric", true);
        }

        return ResolveFallback(normal);
    }

    private MetricDecision ResolveFallback(NormalFontMetadata normal)
    {
        // The drawing substitute is the final source-metric fallback. It is measured
        // at the workbook Normal size, rather than at an individual cell's size.
        if (options.FallbackMaximumDigitWidthPixels96Dpi is { } fallback)
        {
            return new(fallback, "fallback-override", "explicit fallback MDW", true);
        }

        fontManager ??= new FontManager();
        return Measure(fontManager.Resolve(new(normal.Family, normal.Weight, normal.Italic)), normal, "drawing-font") with { IsFallback = true };
    }

    /// <summary>Conversion-owned decisions, also used by selection preflight.</summary>
    internal sealed class MetricCache
    {
        /// <summary>Gets decisions separated by Normal style, settings and registration generation.</summary>
        internal Dictionary<(NormalFontMetadata? Font, ColumnWidthMetricsOptions Options, int Generation), MetricDecision> Values { get; } = new();
    }

    /// <summary>A handle-free metric decision.</summary>
    /// <param name="Width">Maximum digit width.</param>
    /// <param name="Source">Decision source.</param>
    /// <param name="Font">Physical measurement identity.</param>
    /// <param name="IsFallback">Whether a substitute font or fallback override was used.</param>
    internal sealed record MetricDecision(double Width, string Source, string Font, bool IsFallback = false);
}
