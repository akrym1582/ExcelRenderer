namespace ExcelRenderer.Model;

/// <summary>
/// ReportCell が表すデータと操作を提供します.
/// </summary>
public sealed record ReportCell(
    string? Text,
    CellStyle Style,
    int RowSpan = 1,
    int ColumnSpan = 1,
    string? Formula = null)
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<CellBorder>? MergedBorders { get; init; }
}
