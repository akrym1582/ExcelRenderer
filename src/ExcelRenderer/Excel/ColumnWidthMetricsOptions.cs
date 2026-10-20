using ExcelRenderer.Core.Fonts.TrueType;
using SkiaSharp;

namespace ExcelRenderer.Excel;

/// <summary>Immutable Normal-font column metrics settings. Explicit MDW applies to every sheet in the workbook.</summary>
public sealed record ColumnWidthMetricsOptions
{
    /// <summary>Gets the metric policy; source-font metrics are the default.</summary>
    public ColumnWidthMetricsMode Mode { get; init; } = ColumnWidthMetricsMode.SourceFont;

    /// <summary>Gets the original Normal font's maximum digit width at 96 DPI. Must be finite and at least one.</summary>
    public double? MaximumDigitWidthPixels96Dpi { get; init; }

    /// <summary>Gets a font file used exclusively for layout measurements, without registering it for drawing.</summary>
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

        if (SourceFontFile is not null)
        {
            if (Mode == ColumnWidthMetricsMode.RenderingFontLegacy)
            {
                throw new ArgumentException("SourceFontFile cannot be combined with RenderingFontLegacy.");
            }

            _ = LoadSourceFont();
        }
    }

    /// <summary>Loads and validates the selected source face using the shared collection extractor.</summary>
    /// <returns>Standalone font data.</returns>
    internal byte[] LoadSourceFont()
    {
        try
        {
            var bytes = FontFaceExtractor.Extract(File.ReadAllBytes(SourceFontFile!), SourceFontCollectionIndex ?? 0);
            using var stream = new MemoryStream(bytes, writable: false);
            using var face = SKTypeface.FromStream(stream);
            if (face is null)
            {
                throw new InvalidDataException("Unsupported source font data.");
            }

            return bytes;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"Invalid SourceFontFile or SourceFontCollectionIndex: '{SourceFontFile}', face {SourceFontCollectionIndex ?? 0}.", nameof(SourceFontFile), error);
        }
    }
}
