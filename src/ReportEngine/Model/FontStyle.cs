namespace ReportEngine.Model;

public sealed record FontStyle(
    string Family = "Noto Sans JP",
    double Size = 10,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    ReportColor? Color = null);
