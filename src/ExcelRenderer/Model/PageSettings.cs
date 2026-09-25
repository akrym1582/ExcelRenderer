namespace ExcelRenderer.Model;

/// <summary>
/// PageSettings が表すデータと操作を提供します.
/// </summary>
public sealed record PageSettings(
    double Width = 595.276,
    double Height = 841.89,
    double MarginLeft = 36,
    double MarginTop = 36,
    double MarginRight = 36,
    double MarginBottom = 36,
    double? Scale = 1,
    int? FitToPagesWide = null,
    int? FitToPagesTall = null,
    IndexRange? TitleRows = null,
    IndexRange? TitleColumns = null);
