namespace ExcelRenderer.Core;

/// <summary>Controls saved-value projection of workbook cells.</summary>
public sealed record ExcelReadOptions
{
    /// <summary>Gets a value indicating whether substitute-font metrics and fractional column widths follow LibreOffice conventions.</summary>
    public bool UseLibreOfficeColumnWidths { get; init; }

    /// <summary>Gets an explicit Normal digit width at 96 DPI, independent of the drawing font. Must be finite and at least one.</summary>
    public double? MaximumDigitWidthPixels96Dpi { get; init; }

    /// <summary>Gets the digit width at 96 DPI used only when the original Normal font is unavailable. Defaults to 8; null measures the drawing font at the Normal size.</summary>
    public double? FallbackMaximumDigitWidthPixels96Dpi { get; init; } = 8;

    /// <summary>Gets a font file used only to measure column widths at the workbook Normal size.</summary>
    public string? SourceFontFile { get; init; }

    /// <summary>Gets the zero-based source collection face index; requires SourceFontFile.</summary>
    public int? SourceFontCollectionIndex { get; init; }

    /// <summary>Gets a value indicating whether to match or discover the original Normal font. False always uses fallback metrics and cannot be combined with SourceFontFile.</summary>
    public bool UseSourceFontForColumnWidths { get; init; } = true;

    /// <summary>Gets a value indicating whether to look up the original Normal family in system fonts for column measurements only.</summary>
    public bool UseSystemFontsForColumnWidths { get; init; } = true;

    /// <summary>Gets the conversion's explicit finite source region for default-style materialization.</summary>
    internal Func<string, (int FirstRow, int FirstColumn, int LastRow, int LastColumn)?>? GetSelectedRange { get; init; }
}
