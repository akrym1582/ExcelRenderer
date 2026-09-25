namespace ExcelRenderer.Model;

/// <summary>
/// セルの表示文字列、数式、スタイル、および結合範囲の大きさを表します。
/// </summary>
public sealed record ReportCell(
    string? Text,
    CellStyle Style,
    int RowSpan = 1,
    int ColumnSpan = 1,
    string? Formula = null)
{
    /// <summary>
    /// Gets the merged-cell border fragments. 結合セルを構成する各セル位置の罫線情報を取得します。
    /// </summary>
    public IReadOnlyList<CellBorder>? MergedBorders { get; init; }
}
