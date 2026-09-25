using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// FillRectangleCommand が表すデータと操作を提供します.
/// </summary>
public sealed record FillRectangleCommand(int PageNumber, ReportRect Bounds, ReportColor Color) : DrawCommand(PageNumber);
