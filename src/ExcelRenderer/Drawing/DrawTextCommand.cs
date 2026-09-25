using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 指定した矩形内へセルスタイルに従って文字列を描画するコマンドを表します。
/// </summary>
public sealed record DrawTextCommand(int PageNumber, ReportRect Bounds, string Text, CellStyle Style) : DrawCommand(PageNumber);
