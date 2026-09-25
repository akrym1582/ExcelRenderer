using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RenderCell が表すデータと操作を提供します.
/// </summary>
public sealed record RenderCell(ReportCell Cell, ReportRect Bounds)
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<RenderBorder>? MergedBorders { get; init; }
}
