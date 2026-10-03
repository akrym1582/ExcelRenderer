namespace ExcelRenderer.Model;

/// <summary>
/// 帳票の 1 ワークシートを構成するセル、行列、結合範囲、印刷設定、画像、および図形を表します。
/// </summary>
public sealed record ReportSheet(
    string Name,
    IReadOnlyDictionary<CellAddress, ReportCell> Cells,
    IReadOnlyDictionary<int, ColumnDefinition> Columns,
    IReadOnlyDictionary<int, RowDefinition> Rows,
    IReadOnlyList<CellRange> MergedRanges,
    PageSettings PageSettings,
    CellRange? PrintArea = null,
    IReadOnlyList<ReportImage>? Images = null,
    HeaderFooter? HeaderFooter = null,
    IReadOnlyList<ReportShape>? Shapes = null)
{
    /// <summary>Gets the default width in points for columns without an explicit definition.</summary>
    public double DefaultColumnWidth { get; init; } = 64;

    /// <summary>Gets the default height in points for rows without an explicit definition.</summary>
    public double DefaultRowHeight { get; init; } = 15;

    /// <summary>
    /// Gets the independently paginated print areas in workbook order. An empty collection uses
    /// <see cref="PrintArea"/> or the automatically resolved used range.
    /// </summary>
    public IReadOnlyList<CellRange> PrintAreas { get; init; } = [];
}
