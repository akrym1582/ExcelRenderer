namespace ExcelRenderer.Model;

/// <summary>
/// ReportImage が表すデータと操作を提供します.
/// </summary>
public sealed record ReportImage(
    CellAddress Anchor,
    double OffsetX,
    double OffsetY,
    double Width,
    double Height,
    byte[] ImageBytes,
    int ZIndex = 0,
    string? Name = null,
    string? ContentType = null,
    string? Extension = null);
