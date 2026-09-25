using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// セルのレイアウトを分割した方向を表します。
/// </summary>
public enum LayoutSplitDirection
{
    /// <summary>
    /// 分割されていない末端ノードを表します。
    /// </summary>
    None,

    /// <summary>
    /// 水平方向の空白を境に、上下へ分割したことを表します。
    /// </summary>
    Horizontal,

    /// <summary>
    /// 垂直方向の空白を境に、左右へ分割したことを表します。
    /// </summary>
    Vertical,
}
