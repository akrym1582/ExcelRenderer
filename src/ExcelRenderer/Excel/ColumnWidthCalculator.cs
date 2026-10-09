namespace ExcelRenderer.Excel;

/// <summary>
/// SpreadsheetML の列幅を PDF ポイントへ変換します。
/// </summary>
internal static class ColumnWidthCalculator
{
    /// <summary>Converts the stored width to PDF points.</summary>
    /// <param name="rawWidth">The stored character width.</param>
    /// <param name="maximumDigitWidth">The normal font's digit width at 96 DPI.</param>
    /// <returns>The width in points.</returns>
    public static double ToPoints(double rawWidth, double maximumDigitWidth) =>
        Core.Excel.ColumnWidthCalculator.ToPoints(rawWidth, maximumDigitWidth);

    /// <summary>Adds cell padding to a character-only base width.</summary>
    /// <param name="baseColumnWidth">The character-only width.</param>
    /// <param name="maximumDigitWidth">The normal font's digit width at 96 DPI.</param>
    /// <returns>The stored width.</returns>
    public static double FromBaseColumnWidth(double baseColumnWidth, double maximumDigitWidth) =>
        Core.Excel.ColumnWidthCalculator.FromBaseColumnWidth(baseColumnWidth, maximumDigitWidth);
}
