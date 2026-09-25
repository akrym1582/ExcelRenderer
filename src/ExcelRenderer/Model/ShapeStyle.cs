namespace ExcelRenderer.Model;

/// <summary>
/// ShapeStyle が表すデータと操作を提供します.
/// </summary>
public sealed record ShapeStyle(ReportColor? FillColor, ReportColor? LineColor, double LineWidth = 1);
