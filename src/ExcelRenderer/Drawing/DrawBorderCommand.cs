using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

public sealed record DrawBorderCommand(int PageNumber, ReportRect Bounds, BorderStyle Border) : DrawCommand(PageNumber);
