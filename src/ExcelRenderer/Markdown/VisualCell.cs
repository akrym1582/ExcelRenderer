using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// セル範囲の内容、書式、およびワークシート上の表示位置を表します。
/// </summary>
public sealed record VisualCell(CellRange Range, string? Text, double X, double Y,
    double Width, double Height, CellStyle Style, string? Formula = null)
{
    /// <summary>
    /// Gets the cell bounds. セルが占める表示領域を取得します。
    /// </summary>
    public LayoutRect BoundingBox => new(X, Y, Width, Height);
}
