using ExcelRenderer.Core.Fonts;

namespace ExcelRenderer.Excel;

/// <summary>Immutable Normal-font column metrics settings. Explicit MDW applies to every sheet in the workbook.</summary>
public sealed record ColumnWidthMetricsOptions
{
    /// <summary>Gets the metric policy; source-font metrics are the default.</summary>
    public ColumnWidthMetricsMode Mode { get; init; } = ColumnWidthMetricsMode.SourceFont;

    /// <summary>Gets the original Normal font's maximum digit width at 96 DPI. Must be finite and at least one.</summary>
    public double? MaximumDigitWidthPixels96Dpi { get; init; }

    /// <summary>Gets the digit width at 96 DPI used only when the original Normal font is unavailable. Defaults to 8; null measures the drawing font at the Normal size.</summary>
    public double? FallbackMaximumDigitWidthPixels96Dpi { get; init; } = 8;

    /// <summary>Gets a font file used exclusively for layout measurements, without registering it for drawing. LibreOfficeCompatible permits a substitute font.</summary>
    public string? SourceFontFile { get; init; }

    /// <summary>Gets the zero-based source collection face index; standalone fonts require zero.</summary>
    public int? SourceFontCollectionIndex { get; init; }

    /// <summary>Validates settings before input or output is opened.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(typeof(ColumnWidthMetricsMode), Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode));
        }

        if (MaximumDigitWidthPixels96Dpi is { } width && (double.IsNaN(width) || double.IsInfinity(width) || width < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDigitWidthPixels96Dpi), "MDW must be finite and at least one pixel.");
        }

        if (SourceFontCollectionIndex is < 0 || (SourceFontCollectionIndex is not null && SourceFontFile is null))
        {
            throw new ArgumentException("SourceFontCollectionIndex requires SourceFontFile and a nonnegative index.");
        }

        if (FallbackMaximumDigitWidthPixels96Dpi is { } fallback && (double.IsNaN(fallback) || double.IsInfinity(fallback) || fallback < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(FallbackMaximumDigitWidthPixels96Dpi), "Fallback MDW must be finite and at least one pixel.");
        }

        if (SourceFontFile is not null)
        {
            if (Mode is ColumnWidthMetricsMode.RenderingFontLegacy or ColumnWidthMetricsMode.Fallback)
            {
                throw new ArgumentException("SourceFontFile cannot be combined with RenderingFontLegacy or Fallback.");
            }

            _ = LoadSourceFont();
        }
    }

    /// <summary>Loads and validates the selected source face using the shared collection extractor.</summary>
    /// <returns>Standalone font data.</returns>
    internal byte[] LoadSourceFont() => ColumnFontMetrics.Load(SourceFontFile!, SourceFontCollectionIndex ?? 0);
}
