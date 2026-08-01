using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Drawing;

public sealed record DrawImageCommand(int PageNumber, ReportRect Bounds, byte[] ImageBytes) : DrawCommand(PageNumber);
