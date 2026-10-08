namespace ExcelRenderer.Slim.Model;

/// <summary>
/// 文字列のフォントファミリー、サイズ、装飾、および色を表します。
/// </summary>
internal sealed record FontStyle(
    string Family = Fonts.SingleFontContext.Family,
    double Size = 10,
    bool Underline = false,
    ReportColor? Color = null);
