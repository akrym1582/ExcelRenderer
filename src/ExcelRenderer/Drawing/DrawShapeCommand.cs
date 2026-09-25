using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// DrawShapeCommand が表すデータと操作を提供します.
/// </summary>
public sealed record DrawShapeCommand(int PageNumber, ReportRect Bounds, ReportShape Shape) : DrawCommand(PageNumber);
