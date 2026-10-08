using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Drawing;

/// <summary>
/// 指定した矩形を単色で塗りつぶすコマンドを表します。
/// </summary>
internal sealed record FillRectangleCommand(int PageNumber, ReportRect Bounds, ReportColor Color) : DrawCommand(PageNumber);
