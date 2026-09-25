using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// DrawImageCommand が表すデータと操作を提供します.
/// </summary>
public sealed record DrawImageCommand(int PageNumber, ReportRect Bounds, byte[] ImageBytes) : DrawCommand(PageNumber);
