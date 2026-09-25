namespace ExcelRenderer.Model;

/// <summary>
/// 図形の塗りつぶし色、輪郭線の色、および輪郭線の太さを表します。
/// </summary>
public sealed record ShapeStyle(ReportColor? FillColor, ReportColor? LineColor, double LineWidth = 1);
