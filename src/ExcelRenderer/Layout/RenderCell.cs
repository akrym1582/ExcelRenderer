using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ページ上に配置されたセルの内容、スタイル、および描画矩形を表します。
/// </summary>
public sealed record RenderCell(ReportCell Cell, ReportRect Bounds)
{
    /// <summary>
    /// Gets the merged-cell border fragments. 結合セルを構成する各セル位置の罫線と配置矩形を取得します。
    /// </summary>
    public IReadOnlyList<RenderBorder>? MergedBorders { get; init; }
}
