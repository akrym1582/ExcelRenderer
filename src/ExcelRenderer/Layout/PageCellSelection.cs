using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Selects cell origins and explicit-range merged intersections for one band pair.</summary>
internal sealed class PageCellSelection
{
    private const double Epsilon = 1e-7;
    private readonly PageBand horizontal;
    private readonly PageBand vertical;
    private readonly HashSet<int> titleColumns;
    private readonly HashSet<int> titleRows;
    private readonly bool hasRequestedRange;

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
        this.horizontal = horizontal;
        this.vertical = vertical;
        this.titleColumns = titleColumns;
        this.titleRows = titleRows;
        this.hasRequestedRange = hasRequestedRange;
        RepeatColumns = horizontal.Start >= titleColumnEnd - Epsilon;
        RepeatRows = vertical.Start >= titleRowEnd - Epsilon;
    }

    /// <summary>Gets a value indicating whether title columns repeat on this band.</summary>
    internal bool RepeatColumns { get; }

    /// <summary>Gets a value indicating whether title rows repeat on this band.</summary>
    internal bool RepeatRows { get; }

    /// <summary>Tests the cell origin or an explicit-range merged intersection.</summary>
    /// <param name="address">The original cell address.</param>
    /// <param name="bounds">The original sheet-coordinate bounds.</param>
    /// <param name="isMerged">Whether the cell spans more than one row or column.</param>
    /// <returns>Whether this page contains the cell.</returns>
    internal bool Contains(CellAddress address, ReportRect bounds, bool isMerged) =>
        (hasRequestedRange && isMerged &&
            bounds.X < horizontal.End && bounds.X + bounds.Width > horizontal.Start &&
            bounds.Y < vertical.End && bounds.Y + bounds.Height > vertical.Start) ||
        (((bounds.X >= horizontal.Start && bounds.X < horizontal.End) || (RepeatColumns && titleColumns.Contains(address.Column))) &&
            ((bounds.Y >= vertical.Start && bounds.Y < vertical.End) || (RepeatRows && titleRows.Contains(address.Row))));
}
