namespace ExcelRenderer.Slim.Excel;

/// <summary>
/// SpreadsheetML の列幅を PDF ポイントへ変換します。
/// </summary>
internal static class ColumnWidthCalculator
{
    private const double PointsPerPixel = 72d / 96d;

    /// <summary>
    /// raw 列幅と標準フォントの最大数字幅からポイント単位の列幅を計算します。
    /// </summary>
    /// <param name="rawWidth">col 要素に保存された文字数単位の幅です。</param>
    /// <param name="maximumDigitWidth">96 DPI における標準フォントの最大数字幅です。</param>
    /// <returns>ポイント単位の列幅です。</returns>
    public static double ToPoints(double rawWidth, double maximumDigitWidth)
    {
        if (!double.IsFinite(rawWidth) || rawWidth <= 0 ||
            !double.IsFinite(maximumDigitWidth) || maximumDigitWidth <= 0)
        {
            return 0;
        }

        var pixels = Math.Truncate(
            (((256 * rawWidth) + Math.Truncate(128 / maximumDigitWidth)) / 256) * maximumDigitWidth);
        return pixels * PointsPerPixel;
    }

    /// <summary>Converts the character-only base column width to the stored width including cell padding.</summary>
    /// <param name="baseColumnWidth">Number of characters in the base column width.</param>
    /// <param name="maximumDigitWidth">Normal-font maximum digit width at 96 DPI.</param>
    /// <returns>A raw SpreadsheetML column width.</returns>
    public static double FromBaseColumnWidth(double baseColumnWidth, double maximumDigitWidth)
    {
        if (!double.IsFinite(baseColumnWidth) || baseColumnWidth <= 0 ||
            !double.IsFinite(maximumDigitWidth) || maximumDigitWidth <= 0)
        {
            return 0;
        }

        return baseColumnWidth + (5 / maximumDigitWidth);
    }
}
