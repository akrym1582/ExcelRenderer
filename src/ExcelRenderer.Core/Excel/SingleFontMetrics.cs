using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Core.Excel;

/// <summary>Resolves source column metrics separately from single-font drawing substitutions.</summary>
internal sealed class SingleFontMetrics : ICoreFontMetrics
{
    private readonly SingleFontContext context;
    private readonly ExcelReadOptions options;
    private readonly DiagnosticCollector? diagnostics;
    private readonly Func<NormalFontMetadata, SKTypeface?> systemFontResolver;
    private readonly byte[]? sourceFont;
    private readonly Dictionary<NormalFontMetadata, MetricDecision> widths = new();

    /// <summary>Initializes a new instance of the <see cref="SingleFontMetrics"/> class.</summary>
    /// <param name="context">The conversion-owned font.</param>
    /// <param name="diagnostics">The conversion diagnostic collector.</param>
    /// <param name="options">Column metric compatibility settings.</param>
    /// <param name="systemFontResolver">Optional owned-face lookup for isolated source-font resolution.</param>
    internal SingleFontMetrics(SingleFontContext context, DiagnosticCollector? diagnostics = null, ExcelReadOptions? options = null, Func<NormalFontMetadata, SKTypeface?>? systemFontResolver = null)
    {
        this.context = context;
        this.options = options ?? new();
        this.diagnostics = diagnostics;
        this.systemFontResolver = systemFontResolver ?? ResolveSystemFont;
        sourceFont = this.options.SourceFontFile is { } path ? ColumnFontMetrics.Load(path, this.options.SourceFontCollectionIndex ?? 0) : null;
    }

    /// <inheritdoc/>
    public NormalFontMetadata DefaultFont => new("Calibri", 11);

    /// <inheritdoc/>
    public bool UseLibreOfficeColumnWidths => options.UseLibreOfficeColumnWidths;

    /// <inheritdoc/>
    public double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName)
    {
        var key = normalFont ?? DefaultFont;

        // Missing Normal metadata must not share a decision with a known Calibri style.
        var decision = normalFont is null && options.MaximumDigitWidthPixels96Dpi is null && sourceFont is null && !UseLibreOfficeColumnWidths
            ? ResolveFallback(key)
            : ResolveCached(key);
        var message = $"Normal '{normalFont?.Family ?? "unknown"}', {key.Size}pt, weight {key.Weight}, italic {key.Italic}; measurement '{decision.Font}', MDW {decision.Width}px at 96 DPI; source={decision.Source}.";
        if (decision.IsFallback)
        {
            message += " Exact Excel layout cannot be guaranteed; specify SourceFontFile or MaximumDigitWidthPixels96Dpi to supply original metrics.";
        }

        diagnostics?.Add(new(
            decision.IsFallback ? "MaximumDigitWidthFallback" : "MaximumDigitWidthResolved",
            decision.IsFallback ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
            DiagnosticStage.Read,
            message,
            sheetName));
        return decision.Width;
    }

    private static SKTypeface? ResolveSystemFont(NormalFontMetadata normal)
    {
        using var style = new SKFontStyle(normal.Weight, (int)SKFontStyleWidth.Normal, normal.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        return SKFontManager.Default.MatchFamily(normal.Family, style);
    }

    private MetricDecision ResolveCached(NormalFontMetadata normal)
    {
        if (!widths.TryGetValue(normal, out var decision))
        {
            decision = Resolve(normal);
            widths.Add(normal, decision);
        }

        return decision;
    }

    private MetricDecision Resolve(NormalFontMetadata normal)
    {
        if (options.MaximumDigitWidthPixels96Dpi is { } width)
        {
            return new(width, "override", "explicit MDW");
        }

        if (sourceFont is not null)
        {
            using var stream = new MemoryStream(sourceFont, writable: false);
            using var face = SKTypeface.FromStream(stream);
            return new(ColumnFontMetrics.Measure(face, normal.Size, UseLibreOfficeColumnWidths), "source-file", face.FamilyName);
        }

        if (UseLibreOfficeColumnWidths)
        {
            return ColumnFontMetrics.MatchesFamily(context.Face.Typeface, normal.Family)
                ? new(context.MaximumDigitWidth(normal.Size, true), "libreoffice-font", context.Face.Typeface.FamilyName)
                : ResolveFallback(normal);
        }

        if (ColumnFontMetrics.MatchesFamily(context.Face.Typeface, normal.Family))
        {
            return new(context.MaximumDigitWidth(normal.Size), "supplied-font", context.Face.Typeface.FamilyName);
        }

        if (options.UseSystemFontsForColumnWidths)
        {
            try
            {
                using var face = systemFontResolver(normal);
                if (face is not null && ColumnFontMetrics.MatchesFamily(face, normal.Family))
                {
                    return new(ColumnFontMetrics.Measure(face, normal.Size), "system-font", face.FamilyName);
                }
            }
            catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
            {
                // Implicit system lookup is best effort; never adopt an unrelated substitute.
            }
        }

        return ResolveFallback(normal);
    }

    private MetricDecision ResolveFallback(NormalFontMetadata normal) => options.FallbackMaximumDigitWidthPixels96Dpi is { } width
        ? new(width, "fallback-override", "explicit fallback MDW", true)
        : new(context.MaximumDigitWidth(normal.Size, UseLibreOfficeColumnWidths), "drawing-font", context.Face.Typeface.FamilyName, true);

    private sealed record MetricDecision(double Width, string Source, string Font, bool IsFallback = false);
}
