using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Resolves explicit and fit-to-pages print scales without mutating layout state.</summary>
internal static class PrintScaleResolver
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="settings">The settings value.</param>
    internal static bool UsesFitMode(PageSettings settings) => settings.ScaleMode == PrintScaleMode.FitToPages ||
        (settings.ScaleMode is null && settings.Scale is not > 0);

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="settings">The settings value.</param>
    /// <param name="columns">The columns value.</param>
    /// <param name="rows">The rows value.</param>
    /// <param name="getColumnStart">The getColumnStart value.</param>
    /// <param name="getColumnEnd">The getColumnEnd value.</param>
    /// <param name="getMergedColumnEnd">The getMergedColumnEnd value.</param>
    /// <param name="getRowStart">The getRowStart value.</param>
    /// <param name="getRowEnd">The getRowEnd value.</param>
    /// <param name="getMergedRowEnd">The getMergedRowEnd value.</param>
    /// <param name="titleColumnEnd">The titleColumnEnd value.</param>
    /// <param name="titleWidth">The titleWidth value.</param>
    /// <param name="titleRowEnd">The titleRowEnd value.</param>
    /// <param name="titleHeight">The titleHeight value.</param>
    internal static double Resolve(
        PageSettings settings,
        IReadOnlyList<int> columns,
        IReadOnlyList<int> rows,
        Func<int, double> getColumnStart,
        Func<int, double> getColumnEnd,
        Func<int, double, double> getMergedColumnEnd,
        Func<int, double> getRowStart,
        Func<int, double> getRowEnd,
        Func<int, double, double> getMergedRowEnd,
        double titleColumnEnd,
        double titleWidth,
        double titleRowEnd,
        double titleHeight)
    {
        var usesExplicitScale = settings.ScaleMode == PrintScaleMode.Explicit ||
            (settings.ScaleMode is null && settings.Scale is > 0);
        if (usesExplicitScale && settings.Scale is > 0 && double.IsFinite(settings.Scale.Value))
        {
            return settings.Scale.Value;
        }

        if (settings.ScaleMode == PrintScaleMode.Explicit)
        {
            return 1;
        }

        var scales = new List<double>();
        AddFitScale(
            settings.FitToPagesWide,
            settings.Width - settings.MarginLeft - settings.MarginRight,
            columns,
            getColumnStart,
            getColumnEnd,
            getMergedColumnEnd,
            titleColumnEnd,
            titleWidth,
            scales);
        AddFitScale(
            settings.FitToPagesTall,
            settings.Height - settings.MarginTop - settings.MarginBottom,
            rows,
            getRowStart,
            getRowEnd,
            getMergedRowEnd,
            titleRowEnd,
            titleHeight,
            scales);
        return scales.Count == 0 ? 1 : scales.Min();
    }

    private static void AddFitScale(
        int? pageCount,
        double pageSize,
        IReadOnlyList<int> indices,
        Func<int, double> getStart,
        Func<int, double> getEnd,
        Func<int, double, double> getMergedEnd,
        double repeatedEnd,
        double repeatedSize,
        ICollection<double> scales)
    {
        if (pageCount is not > 0 || indices.Count == 0)
        {
            return;
        }

        var contentSize = getEnd(indices[indices.Count - 1]) - getStart(indices[0]);
        if (contentSize > 0)
        {
            scales.Add(GetFitScale(
                pageCount.Value,
                pageSize,
                contentSize,
                indices,
                getStart,
                getEnd,
                getMergedEnd,
                repeatedEnd,
                repeatedSize));
        }
    }

    private static double GetFitScale(
        int pageCount,
        double pageSize,
        double contentSize,
        IReadOnlyList<int> indices,
        Func<int, double> getStart,
        Func<int, double> getEnd,
        Func<int, double, double> getMergedEnd,
        double repeatedEnd,
        double repeatedSize)
    {
        if (pageCount <= 0 || !double.IsFinite(pageSize) || pageSize <= 0 ||
            !double.IsFinite(contentSize) || contentSize <= 0)
        {
            return 1;
        }

        var lower = 0d;
        var upper = 1d;
        if (Fits(upper))
        {
            return upper;
        }

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var candidate = (lower + upper) / 2;
            if (Fits(candidate))
            {
                lower = candidate;
            }
            else
            {
                upper = candidate;
            }
        }

        return lower;

        bool Fits(double scale)
        {
            var availableSize = pageSize / scale;
            var bands = PageBandBuilder.Create(
                indices,
                getStart,
                getEnd,
                availableSize,
                getMergedEnd,
                repeatedEnd,
                repeatedSize);
            if (bands.Count > pageCount)
            {
                return false;
            }

            return bands.All(band => indices
                .Where(index => getStart(index) >= band.Start && getStart(index) < band.End)
                .All(index =>
                {
                    var titleSize = band.Start >= repeatedEnd - 1e-7 ? repeatedSize : 0;
                    return getMergedEnd(index, getEnd(index)) - band.Start <= availableSize - titleSize;
                }));
        }
    }
}
