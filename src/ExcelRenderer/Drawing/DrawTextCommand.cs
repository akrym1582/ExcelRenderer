using ExcelRenderer.Abstractions;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 指定した矩形内へセルスタイルに従って文字列を描画するコマンドを表します。
/// </summary>
public sealed record DrawTextCommand(int PageNumber, ReportRect Bounds, string Text, CellStyle Style) : DrawCommand(PageNumber)
{
    /// <summary>Gets the finalized line and font-run layout.</summary>
    public TextLayoutResult? TextLayout { get; init; }
}
