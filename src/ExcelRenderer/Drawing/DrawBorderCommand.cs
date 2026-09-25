using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 指定した矩形の各辺へセルの罫線を描画するコマンドを表します。
/// </summary>
public sealed record DrawBorderCommand(int PageNumber, ReportRect Bounds, BorderStyle Border) : DrawCommand(PageNumber);
