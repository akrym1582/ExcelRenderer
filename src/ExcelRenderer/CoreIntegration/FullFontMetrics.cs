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
    public double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName)
    {
        var normal = normalFont ?? DefaultFont;
        var key = (normalFont, options, (fontManager as FontManager)?.RegistrationGeneration ?? 0);
        if (!cache.Values.TryGetValue(key, out var decision))
        {
            decision = normalFont is null && options.MaximumDigitWidthPixels96Dpi is null && options.SourceFontFile is null
                ? new(7, options.Mode == ColumnWidthMetricsMode.RenderingFontLegacy ? "legacy" : "compatibility", "7px compatibility metric (Normal metadata unavailable)", true)
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

    private static MetricDecision Measure(ResolvedFont resolved, NormalFontMetadata normal, string source)
    {
        using var ownedResources = ConversionFontResources.Current is null ? new ConversionFontResources() : null;
        var typeface = ConversionFontResources.Current!.GetTypeface(resolved);
        using var font = new SKFont(typeface, (float)(normal.Size * 96 / 72));
        var width = Math.Max(1, Math.Round(Enumerable.Range(0, 10).Max(digit => font.MeasureText(digit.ToString())), MidpointRounding.AwayFromZero));
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
            if (options.Mode == ColumnWidthMetricsMode.SourceFont)
            {
                fontManager ??= new FontManager();
            }

            var request = new FontRequest(normal.Family, normal.Weight, normal.Italic);
            if (options.Mode == ColumnWidthMetricsMode.RenderingFontLegacy && fontManager is not null)
            {
                // Historical MDW resolution requested the regular drawing family.
                return Measure(fontManager.Resolve(new(normal.Family)), normal, "legacy");
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

        return new(7, options.Mode == ColumnWidthMetricsMode.RenderingFontLegacy ? "legacy" : "compatibility", "7px compatibility metric", true);
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
    /// <param name="IsFallback">Whether no physical measurement was available.</param>
    internal sealed record MetricDecision(double Width, string Source, string Font, bool IsFallback = false);
}
