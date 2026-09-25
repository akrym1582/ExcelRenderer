using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// 空白によるレイアウト分割の結果を保持するツリーのノードです。
/// </summary>
public sealed class LayoutNode
{
    /// <summary>
    /// Gets the node bounds. このノードに含まれるセル全体の表示領域を取得します。
    /// </summary>
    public LayoutRect BoundingBox { get; init; }

    /// <summary>
    /// Gets the split direction. 子ノードを分けた空白の方向を取得します。
    /// </summary>
    public LayoutSplitDirection SplitDirection { get; init; }

    /// <summary>
    /// Gets the child nodes. 分割後の子ノードを取得します。末端ノードの場合は空のリストです。
    /// </summary>
    public IReadOnlyList<LayoutNode> Children { get; init; } = Array.Empty<LayoutNode>();

    /// <summary>
    /// Gets the visual cells. このノードの範囲に含まれる表示セルを取得します。
    /// </summary>
    public IReadOnlyList<VisualCell> Cells { get; init; } = Array.Empty<VisualCell>();
}
