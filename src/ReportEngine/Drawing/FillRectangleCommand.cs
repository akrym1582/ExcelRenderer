using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Drawing;

public sealed record FillRectangleCommand(int PageNumber, ReportRect Bounds, ReportColor Color) : DrawCommand(PageNumber);
