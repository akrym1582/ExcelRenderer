using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Core.Excel;

/// <summary>Resolves source column metrics separately from explicit drawing-font substitutions.</summary>
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

        if (!options.UseSourceFontForColumnWidths)
        {
            message += " Source-font lookup is disabled.";
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

        if (!options.UseSourceFontForColumnWidths)
        {
            return ResolveFallback(normal);
        }

        if (sourceFont is not null)
        {
            using var stream = new MemoryStream(sourceFont, writable: false);
            using var face = SKTypeface.FromStream(stream);
            return new(ColumnFontMetrics.Measure(face, normal.Size, UseLibreOfficeColumnWidths), "source-file", face.FamilyName);
        }

        var supplied = context.FindExact(normal.Family);
        if (supplied is not null)
        {
            return new(
                ColumnFontMetrics.Measure(supplied.Typeface, normal.Size, UseLibreOfficeColumnWidths),
                UseLibreOfficeColumnWidths ? "libreoffice-font" : "supplied-font",
                supplied.Typeface.FamilyName);
        }

        if (UseLibreOfficeColumnWidths)
        {
            return ResolveFallback(normal);
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

    private MetricDecision ResolveFallback(NormalFontMetadata normal)
    {
        if (options.FallbackMaximumDigitWidthPixels96Dpi is { } width)
        {
            return new(width, "fallback-override", "configured fallback MDW", true);
        }

        var face = context.Resolve(normal.Family, normal.FamilyClassification);
        return new(ColumnFontMetrics.Measure(face.Typeface, normal.Size, UseLibreOfficeColumnWidths), "drawing-font", face.Typeface.FamilyName, true);
    }

    private sealed record MetricDecision(double Width, string Source, string Font, bool IsFallback = false);
}
