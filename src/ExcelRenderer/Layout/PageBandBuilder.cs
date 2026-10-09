namespace ExcelRenderer.Layout;

/// <summary>Creates immutable page bands for one worksheet axis.</summary>
internal static class PageBandBuilder
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="indices">The indices value.</param>
    /// <param name="getStart">The getStart value.</param>
    /// <param name="getEnd">The getEnd value.</param>
    /// <param name="availableSize">The availableSize value.</param>
    /// <param name="getMergedEnd">The getMergedEnd value.</param>
    /// <param name="repeatedEnd">The repeatedEnd value.</param>
    /// <param name="repeatedSize">The repeatedSize value.</param>
    /// <param name="manualBreaks">The manualBreaks value.</param>
    internal static IReadOnlyList<PageBand> Create(
        IReadOnlyList<int> indices,
        Func<int, double> getStart,
        Func<int, double> getEnd,
        double availableSize,
        Func<int, double, double> getMergedEnd,
        double repeatedEnd = double.NegativeInfinity,
        double repeatedSize = 0,
        IReadOnlyCollection<int>? manualBreaks = null)
    {
        return Core.Layout.PageBandBuilder.Create(
            indices,
            getStart,
            getEnd,
            availableSize,
            getMergedEnd,
            repeatedEnd,
            repeatedSize,
            manualBreaks).Select(band => new PageBand(band.Start, band.End)).ToArray();
    }
}
