using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// ページ上に配置されたセルの内容、スタイル、および描画矩形を表します。
/// </summary>
internal sealed record RenderCell(ReportCell Cell, ReportRect Bounds)
{
    /// <summary>Gets the original one-based worksheet address.</summary>
    public CellAddress? SourceAddress { get; init; }

    /// <summary>Gets the text content rectangle in page coordinates.</summary>
    public ReportRect ContentBounds { get; init; }

    /// <summary>
    /// Gets the merged-cell border fragments. 結合セルを構成する各セル位置の罫線と配置矩形を取得します。
    /// </summary>
    public IReadOnlyList<RenderBorder>? MergedBorders { get; init; }

    /// <summary>Gets the finalized text layout, when the measurer provides one.</summary>
    public TextLayoutResult? TextLayout { get; init; }

    /// <summary>Gets an optional page-space viewport supplied by a selection policy.</summary>
    internal ReportRect? ClipBounds { get; init; }
}
