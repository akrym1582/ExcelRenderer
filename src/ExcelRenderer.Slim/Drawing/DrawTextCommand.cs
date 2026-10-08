using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Drawing;

/// <summary>
/// 指定した矩形内へセルスタイルに従って文字列を描画するコマンドを表します。
/// </summary>
internal sealed record DrawTextCommand(int PageNumber, ReportRect Bounds, string Text, CellStyle Style) : DrawCommand(PageNumber)
{
    /// <summary>Gets the finalized line and font-run layout.</summary>
    public TextLayoutResult? TextLayout { get; init; }
}
