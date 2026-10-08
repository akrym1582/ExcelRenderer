using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Drawing;

/// <summary>
/// 指定した矩形の各辺へセルの罫線を描画するコマンドを表します。
/// </summary>
internal sealed record DrawBorderCommand(int PageNumber, ReportRect Bounds, BorderStyle Border) : DrawCommand(PageNumber);
