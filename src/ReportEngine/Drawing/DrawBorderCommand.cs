using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Drawing;

public sealed record DrawBorderCommand(int PageNumber, ReportRect Bounds, BorderStyle Border) : DrawCommand(PageNumber);
