using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// ページ上に配置された画像の描画矩形、バイナリデータ、および重なり順を表します。
/// </summary>
internal sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes, int ZIndex = 0);
