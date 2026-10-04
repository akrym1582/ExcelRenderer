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
    /// <summary>Gets original cell hyperlinks without allocating empty cells for range references.</summary>
    public IReadOnlyList<ReportHyperlink> Hyperlinks { get; init; } = [];

    /// <summary>Gets the default width in points for columns without an explicit definition.</summary>
    public double DefaultColumnWidth { get; init; } = 64;

    /// <summary>Gets the default height in points for rows without an explicit definition.</summary>
    public double DefaultRowHeight { get; init; } = 15;

    /// <summary>
    /// Gets the independently paginated print areas in workbook order. An empty collection uses
    /// <see cref="PrintArea"/> or the automatically resolved used range.
    /// </summary>
    public IReadOnlyList<CellRange> PrintAreas { get; init; } = [];

    /// <summary>Gets workbook and worksheet scoped name definitions for internal hyperlinks.</summary>
    internal IReadOnlyDictionary<string, string> HyperlinkNames { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the original one-based workbook sheet number.</summary>
    internal int SourceSheetIndex { get; init; }

    /// <summary>Gets the explicit selection applied by the rendering pipeline.</summary>
    internal CellRange? RequestedRange { get; init; }
}
