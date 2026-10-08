namespace ExcelRenderer.Slim.Layout;

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
        var bands = new List<PageBand>();
        for (var position = 0; position < indices.Count;)
        {
            var start = getStart(indices[position]);
            var end = start;
            var firstPosition = position;
            var pageAvailableSize = availableSize - (start >= repeatedEnd - 1e-7 ? repeatedSize : 0);
            while (position < indices.Count)
            {
                if (position > firstPosition && manualBreaks?.Contains(indices[position - 1]) == true)
                {
                    break;
                }

                var candidateEnd = getMergedEnd(indices[position], Math.Max(end, getEnd(indices[position])));
                if (position > firstPosition && candidateEnd - start > pageAvailableSize + 1e-7)
                {
                    break;
                }

                end = candidateEnd;
                position++;
            }

            bands.Add(new(start, position < indices.Count ? getStart(indices[position]) : double.PositiveInfinity));
        }

        return bands;
    }
}
