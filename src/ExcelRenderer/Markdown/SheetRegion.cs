using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// SheetRegion が表すデータと操作を提供します.
/// </summary>
public sealed class SheetRegion
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public CellRange BoundingRange { get; init; }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public LayoutRect BoundingBox { get; init; }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<VisualCell> Cells { get; init; } = Array.Empty<VisualCell>();

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<ReportImage> Images { get; init; } = Array.Empty<ReportImage>();

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public RegionType Type { get; init; }
}
