namespace ExcelRenderer.Model;

public sealed record ReportImage(
    CellAddress Anchor,
    double OffsetX,
    double OffsetY,
    double Width,
    double Height,
    byte[] ImageBytes,
    int ZIndex = 0);
