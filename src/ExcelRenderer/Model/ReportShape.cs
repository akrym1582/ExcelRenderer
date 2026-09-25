namespace ExcelRenderer.Model;

/// <summary>
/// セルを基準とする位置、寸法、種類、外観、文字、回転角、および重なり順を持つ図形を表します。
/// </summary>
public sealed record ReportShape(CellAddress Anchor, double OffsetX, double OffsetY, double Width,
    double Height, ShapeKind Kind, ShapeStyle Style, ShapeText? Text, double Rotation, int ZIndex,
    ShapeAdjustment? Adjustment = null);
