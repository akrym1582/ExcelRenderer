using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// レイアウト解析によってひとまとまりと判定されたワークシート領域を表します。
/// </summary>
public sealed class SheetRegion
{
    /// <summary>
    /// Gets the cell range. 領域を包含する先頭セルから末尾セルまでの範囲を取得します。
    /// </summary>
    public CellRange BoundingRange { get; init; }

    /// <summary>
    /// Gets the region bounds. 領域を包含する表示上の矩形を取得します。
    /// </summary>
    public LayoutRect BoundingBox { get; init; }

    /// <summary>
    /// Gets the visual cells. 領域に属する表示セルを取得します。
    /// </summary>
    public IReadOnlyList<VisualCell> Cells { get; init; } = Array.Empty<VisualCell>();

    /// <summary>
    /// Gets the embedded images. 中心点が領域内にある埋め込み画像を取得します。
    /// </summary>
    public IReadOnlyList<ReportImage> Images { get; init; } = Array.Empty<ReportImage>();

    /// <summary>
    /// Gets the classified region type. セルの配置と内容から判定された領域種別を取得します。
    /// </summary>
    public RegionType Type { get; init; }
}
