namespace ExcelRenderer.Model;

/// <summary>
/// 文字列のフォントファミリー、サイズ、装飾、および色を表します。
/// </summary>
public sealed record FontStyle(
    string Family = "Noto Sans JP",
    double Size = 10,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    ReportColor? Color = null);
