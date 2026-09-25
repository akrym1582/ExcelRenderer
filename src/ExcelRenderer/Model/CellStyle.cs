namespace ExcelRenderer.Model;

/// <summary>
/// セル文字列のフォント、背景、罫線、配置、折り返し、および縮小表示の設定を表します。
/// </summary>
public sealed record CellStyle(
    FontStyle Font,
    ReportColor? Background = null,
    BorderStyle? Border = null,
    HorizontalAlignment HorizontalAlignment = HorizontalAlignment.Left,
    VerticalAlignment VerticalAlignment = VerticalAlignment.Top,
    bool WrapText = false,
    bool ShrinkToFit = false)
{
    /// <summary>
    /// Gets the default cell style. 未指定のセルに適用する標準フォントと既定の配置を持つスタイルを取得します。
    /// </summary>
    public static CellStyle Default { get; } = new(new FontStyle());
}
