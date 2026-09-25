using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// CellLayout が表すデータと操作を提供します.
/// </summary>
public sealed record CellLayout(CellAddress Address, ReportRect Bounds, TextSize TextSize)
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<RenderBorder>? MergedBorders { get; init; }
}
