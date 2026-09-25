using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RenderBorder が表すデータと操作を提供します.
/// </summary>
public sealed record RenderBorder(ReportRect Bounds, BorderStyle Border);
