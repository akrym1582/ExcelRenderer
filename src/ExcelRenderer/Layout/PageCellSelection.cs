using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Selects cell origins and explicit-range merged intersections for one band pair.</summary>
internal sealed class PageCellSelection
{
    private readonly CoreIntegration.FullCellSelection inner;

    /// <summary>Initializes a new instance of the <see cref="PageCellSelection"/> class.</summary>
    /// <param name="horizontal">The half-open horizontal band.</param>
    /// <param name="vertical">The half-open vertical band.</param>
    /// <param name="titleColumns">Reusable title-column membership.</param>
    /// <param name="titleRows">Reusable title-row membership.</param>
    /// <param name="titleColumnEnd">The exclusive title-column end.</param>
    /// <param name="titleRowEnd">The exclusive title-row end.</param>
    /// <param name="hasRequestedRange">Whether explicit-range merged intersections are selected.</param>
    internal PageCellSelection(PageBand horizontal, PageBand vertical, HashSet<int> titleColumns, HashSet<int> titleRows, double titleColumnEnd, double titleRowEnd, bool hasRequestedRange)
    {
        inner = new(CoreIntegration.CoreModelAdapter.ToCore(horizontal), CoreIntegration.CoreModelAdapter.ToCore(vertical), titleColumns, titleRows, titleColumnEnd, titleRowEnd, hasRequestedRange);
    }

    /// <summary>Gets a value indicating whether title columns repeat on this band.</summary>
    internal bool RepeatColumns => inner.RepeatColumns;

    /// <summary>Gets a value indicating whether title rows repeat on this band.</summary>
    internal bool RepeatRows => inner.RepeatRows;

    /// <summary>Tests the cell origin or an explicit-range merged intersection.</summary>
    /// <param name="address">The original cell address.</param>
    /// <param name="bounds">The original sheet-coordinate bounds.</param>
    /// <param name="isMerged">Whether the cell spans more than one row or column.</param>
    /// <returns>Whether this page contains the cell.</returns>
    internal bool Contains(CellAddress address, ReportRect bounds, bool isMerged) =>
        inner.Contains(CoreIntegration.CoreModelAdapter.ToCore(address), CoreIntegration.CoreModelAdapter.ToCore(bounds), isMerged);
}
