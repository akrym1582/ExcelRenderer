using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// DrawLineCommand が表すデータと操作を提供します.
/// </summary>
public sealed record DrawLineCommand(int PageNumber, double X1, double Y1, double X2, double Y2, BorderSide Style) : DrawCommand(PageNumber);
