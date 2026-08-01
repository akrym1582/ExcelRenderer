using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Drawing;

public sealed record DrawLineCommand(int PageNumber, double X1, double Y1, double X2, double Y2, BorderSide Style) : DrawCommand(PageNumber);
