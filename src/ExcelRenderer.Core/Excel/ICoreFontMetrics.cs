namespace ExcelRenderer.Core.Excel;

/// <summary>Supplies product font resolution and the shared column conversion policy.</summary>
internal interface ICoreFontMetrics
{
    /// <summary>Gets the default Normal style used as a cache key.</summary>
    NormalFontMetadata DefaultFont { get; }

    /// <summary>Gets a value indicating whether column widths retain fractional pixels.</summary>
    bool UseLibreOfficeColumnWidths { get; }

    /// <summary>Measures the Normal font or applies the product fallback and diagnostics.</summary>
    /// <param name="normalFont">The original workbook Normal font.</param>
    /// <param name="sheetName">The diagnostic sheet.</param>
    /// <returns>The selected maximum digit width in pixels.</returns>
    double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName);
}
