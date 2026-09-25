using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// VisualCell が表すデータと操作を提供します.
/// </summary>
public sealed record VisualCell(CellRange Range, string? Text, double X, double Y,
    double Width, double Height, CellStyle Style, string? Formula = null)
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public LayoutRect BoundingBox => new(X, Y, Width, Height);
}
