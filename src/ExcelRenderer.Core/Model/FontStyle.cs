namespace ExcelRenderer.Core.Model;

/// <summary>
/// 文字列のフォントファミリー、サイズ、装飾、および色を表します。
/// </summary>
internal sealed record FontStyle(
    string Family = Fonts.SingleFontContext.Family,
    double Size = 10,
    bool Underline = false,
    ReportColor? Color = null)
{
    /// <summary>Gets a value indicating whether the source font is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Gets a value indicating whether the source font is italic.</summary>
    public bool Italic { get; init; }
}
