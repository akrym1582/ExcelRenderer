using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ページ上に配置された画像の描画矩形、バイナリデータ、および重なり順を表します。
/// </summary>
public sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes, int ZIndex = 0);
