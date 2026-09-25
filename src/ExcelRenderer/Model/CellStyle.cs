namespace ExcelRenderer.Model;

/// <summary>
/// CellStyle が表すデータと操作を提供します.
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
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public static CellStyle Default { get; } = new(new FontStyle());
}
