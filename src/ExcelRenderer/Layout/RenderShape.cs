using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ページ上に配置された図形と、その描画矩形を表します。
/// </summary>
public sealed record RenderShape(ReportRect Bounds, ReportShape Shape);
