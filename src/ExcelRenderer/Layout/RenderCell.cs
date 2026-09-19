using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed record RenderCell(ReportCell Cell, ReportRect Bounds)
{
    public IReadOnlyList<RenderBorder>? MergedBorders { get; init; }
}
