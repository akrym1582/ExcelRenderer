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
    IReadOnlyList<ReportShape>? Shapes = null);
