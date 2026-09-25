namespace ExcelRenderer.Model;

/// <summary>
/// FontStyle が表すデータと操作を提供します.
/// </summary>
public sealed record FontStyle(
    string Family = "Noto Sans JP",
    double Size = 10,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    ReportColor? Color = null);
