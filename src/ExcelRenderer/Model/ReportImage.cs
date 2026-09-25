namespace ExcelRenderer.Model;

/// <summary>
/// セルを基準とする位置、寸法、画像データ、重なり順、および画像メタデータを表します。
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
