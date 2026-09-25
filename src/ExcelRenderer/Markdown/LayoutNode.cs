using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// LayoutNode が表すデータと操作を提供します.
/// </summary>
public sealed class LayoutNode
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public LayoutRect BoundingBox { get; init; }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public LayoutSplitDirection SplitDirection { get; init; }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<LayoutNode> Children { get; init; } = Array.Empty<LayoutNode>();

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<VisualCell> Cells { get; init; } = Array.Empty<VisualCell>();
}
