using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Drawing;

/// <summary>
/// 特定のページへ出力する描画操作の基底データを表します。
/// </summary>
internal abstract record DrawCommand(int PageNumber)
{
    /// <summary>Gets the borrowed extension metadata for this command.</summary>
    public Extensibility.ICoreExtensionData? ExtensionData { get; init; }

    /// <summary>Gets a borrowed cell viewport for product orchestration.</summary>
    internal ReportRect? ClipBounds { get; init; }
}
