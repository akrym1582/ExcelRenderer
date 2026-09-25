using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RenderShape が表すデータと操作を提供します.
/// </summary>
public sealed record RenderShape(ReportRect Bounds, ReportShape Shape);
