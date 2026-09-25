namespace ExcelRenderer.Model;

/// <summary>
/// ReportShape が表すデータと操作を提供します.
/// </summary>
public sealed record ReportShape(CellAddress Anchor, double OffsetX, double OffsetY, double Width,
    double Height, ShapeKind Kind, ShapeStyle Style, ShapeText? Text, double Rotation, int ZIndex,
    ShapeAdjustment? Adjustment = null);
