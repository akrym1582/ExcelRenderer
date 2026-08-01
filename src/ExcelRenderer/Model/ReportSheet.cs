namespace ExcelRenderer.Model;

public sealed record ReportSheet(
    string Name,
    IReadOnlyDictionary<CellAddress, ReportCell> Cells,
    IReadOnlyDictionary<int, ColumnDefinition> Columns,
    IReadOnlyDictionary<int, RowDefinition> Rows,
    IReadOnlyList<CellRange> MergedRanges,
    PageSettings PageSettings,
    CellRange? PrintArea = null,
    IReadOnlyList<ReportImage>? Images = null,
    HeaderFooter? HeaderFooter = null);
