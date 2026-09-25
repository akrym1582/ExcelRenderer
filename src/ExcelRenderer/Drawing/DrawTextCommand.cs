using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// DrawTextCommand が表すデータと操作を提供します.
/// </summary>
public sealed record DrawTextCommand(int PageNumber, ReportRect Bounds, string Text, CellStyle Style) : DrawCommand(PageNumber);
