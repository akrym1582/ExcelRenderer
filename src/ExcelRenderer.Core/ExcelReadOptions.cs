namespace ExcelRenderer.Core;

/// <summary>Controls saved-value projection of workbook cells.</summary>
public sealed record ExcelReadOptions
{
    /// <summary>Gets a value indicating whether substitute-font metrics and fractional column widths follow LibreOffice conventions.</summary>
    public bool UseLibreOfficeColumnWidths { get; init; }

    /// <summary>Gets an explicit Normal digit width at 96 DPI, independent of the drawing font. Must be finite and at least one.</summary>
    public double? MaximumDigitWidthPixels96Dpi { get; init; }

    /// <summary>Gets the conversion's explicit finite source region for default-style materialization.</summary>
    internal Func<string, (int FirstRow, int FirstColumn, int LastRow, int LastColumn)?>? GetSelectedRange { get; init; }
}
