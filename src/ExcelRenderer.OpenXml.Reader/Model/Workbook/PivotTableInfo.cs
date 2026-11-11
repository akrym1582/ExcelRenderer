using ExcelRenderer.OpenXml.Reader.Model.Cells;

namespace ExcelRenderer.OpenXml.Reader.Model.Workbook;

/// <summary>Saved pivot output location and cache refresh state; no aggregate calculation is performed.</summary>
public sealed class PivotTableInfo
{
    /// <summary>Initializes a new instance of the <see cref="PivotTableInfo"/> class.</summary>
    /// <param name="name">Saved table name.</param>
    /// <param name="range">Saved output rectangle.</param>
    /// <param name="requiresRefresh">Cache refresh state.</param>
    internal PivotTableInfo(string name, CellRange range, bool requiresRefresh)
    {
        Name = name;
        Range = range;
        RequiresRefresh = requiresRefresh;
    }

    /// <summary>Gets the saved table name.</summary>
    public string Name { get; }

    /// <summary>Gets the saved output rectangle, excluding page filter labels.</summary>
    public CellRange Range { get; }

    /// <summary>Gets a value indicating whether the cache requests refresh or is invalid.</summary>
    public bool RequiresRefresh { get; }
}
