using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// セルのシート上のアドレス、配置矩形、および計測済み文字寸法を保持します。
/// </summary>
public sealed record CellLayout(CellAddress Address, ReportRect Bounds, TextSize TextSize)
{
    /// <summary>
    /// Gets the merged-cell border fragments. 結合セルを構成する各セル位置の罫線と配置矩形を取得します。
    /// </summary>
    public IReadOnlyList<RenderBorder>? MergedBorders { get; init; }
}
