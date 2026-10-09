using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Adds explicit-range merged intersections to the common origin/title rule.</summary>
internal sealed class FullCellSelection : PageCellSelection
{
    private readonly PageBand horizontal;
    private readonly PageBand vertical;
    private readonly bool requestedRange;

    /// <summary>Initializes a new instance of the <see cref="FullCellSelection"/> class.</summary>
    /// <param name="horizontal">The body horizontal band.</param>
    /// <param name="vertical">The body vertical band.</param>
    /// <param name="titleColumns">Repeated columns.</param>
    /// <param name="titleRows">Repeated rows.</param>
    /// <param name="titleColumnEnd">The exclusive title column end.</param>
    /// <param name="titleRowEnd">The exclusive title row end.</param>
    /// <param name="requestedRange">Whether merged intersections are selected.</param>
    internal FullCellSelection(PageBand horizontal, PageBand vertical, HashSet<int> titleColumns, HashSet<int> titleRows, double titleColumnEnd, double titleRowEnd, bool requestedRange)
        : base(horizontal, vertical, titleColumns, titleRows, titleColumnEnd, titleRowEnd)
    {
        this.horizontal = horizontal;
        this.vertical = vertical;
        this.requestedRange = requestedRange;
    }

    /// <inheritdoc/>
    internal override bool Contains(CellAddress address, ReportRect bounds, bool isMerged) =>
        (requestedRange && isMerged && bounds.X < horizontal.End && bounds.X + bounds.Width > horizontal.Start && bounds.Y < vertical.End && bounds.Y + bounds.Height > vertical.Start) || base.Contains(address, bounds, isMerged);
}
