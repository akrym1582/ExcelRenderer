using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>Selects one rectangular range in a worksheet.</summary>
/// <param name="SheetName">The selected worksheet name.</param>
/// <param name="Range">The inclusive, one-based cell range.</param>
public sealed record SheetRangeSelection(string SheetName, CellRange Range);
