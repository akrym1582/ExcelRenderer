using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

public sealed record FillRectangleCommand(int PageNumber, ReportRect Bounds, ReportColor Color) : DrawCommand(PageNumber);
