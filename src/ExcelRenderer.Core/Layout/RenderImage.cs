using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// ページ上に配置された画像の描画矩形、バイナリデータ、および重なり順を表します。
/// </summary>
internal sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes, int ZIndex = 0)
{
    /// <summary>Gets borrowed product image metadata.</summary>
    internal Extensibility.ICoreExtensionData? ExtensionData { get; init; }
}
