using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed record CellLayout(CellAddress Address, ReportRect Bounds, TextSize TextSize)
{
    public IReadOnlyList<RenderBorder>? MergedBorders { get; init; }
}
