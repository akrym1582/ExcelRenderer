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
    /// <summary>Gets optional effective partial text formatting; concatenation must equal Text.</summary>
    public IReadOnlyList<CellTextRun>? RichTextRuns { get; init; }

    /// <summary>
    /// Gets the merged-cell border fragments. 結合セルを構成する各セル位置の罫線情報を取得します。
    /// </summary>
    public IReadOnlyList<CellBorder>? MergedBorders { get; init; }

    /// <summary>Gets a value indicating whether source partial decorations cannot be rendered.</summary>
    internal bool UnsupportedRichTextFormatting { get; init; }
}
