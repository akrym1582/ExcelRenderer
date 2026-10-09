using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Drawing;

/// <summary>
/// 指定した矩形内へセルスタイルに従って文字列を描画するコマンドを表します。
/// </summary>
internal sealed record DrawTextCommand(int PageNumber, ReportRect Bounds, string Text, CellStyle Style) : DrawCommand(PageNumber)
{
    /// <summary>Gets the finalized line and font-run layout.</summary>
    public TextLayoutResult? TextLayout { get; init; }
}
