using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Resolves explicit and fit-to-pages print scales without mutating layout state.</summary>
internal static class PrintScaleResolver
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="settings">The settings value.</param>
    internal static bool UsesFitMode(PageSettings settings) =>
        Core.Layout.PrintScaleResolver.UsesFitMode(CoreIntegration.CoreModelAdapter.ToCore(settings));

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
        return Core.Layout.PrintScaleResolver.Resolve(
            CoreIntegration.CoreModelAdapter.ToCore(settings),
            columns,
            rows,
            getColumnStart,
            getColumnEnd,
            getMergedColumnEnd,
            getRowStart,
            getRowEnd,
            getMergedRowEnd,
            titleColumnEnd,
            titleWidth,
            titleRowEnd,
            titleHeight);
    }
}
