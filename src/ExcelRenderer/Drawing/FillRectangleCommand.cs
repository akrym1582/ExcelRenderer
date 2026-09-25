using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 指定した矩形を単色で塗りつぶすコマンドを表します。
/// </summary>
public sealed record FillRectangleCommand(int PageNumber, ReportRect Bounds, ReportColor Color) : DrawCommand(PageNumber);
