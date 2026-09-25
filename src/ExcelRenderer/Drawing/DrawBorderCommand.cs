using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// DrawBorderCommand が表すデータと操作を提供します.
/// </summary>
public sealed record DrawBorderCommand(int PageNumber, ReportRect Bounds, BorderStyle Border) : DrawCommand(PageNumber);
