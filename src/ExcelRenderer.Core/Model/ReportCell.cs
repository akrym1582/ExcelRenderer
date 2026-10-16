namespace ExcelRenderer.Core.Model;

/// <summary>
/// セルの表示文字列、数式、スタイル、および結合範囲の大きさを表します。
/// </summary>
internal sealed record ReportCell(
    string? Text,
    CellStyle Style,
    int RowSpan = 1,
    int ColumnSpan = 1,
    string? Formula = null)
{
    /// <summary>Gets the original shared formula index, without expanding follower text.</summary>
    public int? FormulaSharedIndex { get; init; }

    /// <summary>Gets the original shared or array formula master coordinate.</summary>
    public CellAddress? FormulaMasterAddress { get; init; }

    /// <summary>
    /// Gets the merged-cell border fragments. 結合セルを構成する各セル位置の罫線情報を取得します。
    /// </summary>
    public IReadOnlyList<CellBorder>? MergedBorders { get; init; }

    /// <summary>Gets neutral typed product metadata without interpreting partial formatting.</summary>
    internal Extensibility.ICoreExtensionData? ExtensionData { get; init; }
}
