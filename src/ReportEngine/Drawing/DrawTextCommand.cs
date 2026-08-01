using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Drawing;

public sealed record DrawTextCommand(int PageNumber, ReportRect Bounds, string Text, CellStyle Style) : DrawCommand(PageNumber);
