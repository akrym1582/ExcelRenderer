namespace ExcelRenderer.Model;

public enum ShapeKind { Rectangle, RoundedRectangle, Ellipse, WedgeRectangleCallout, WedgeRoundedRectangleCallout }

public sealed record ShapeStyle(ReportColor? FillColor, ReportColor? LineColor, double LineWidth = 1);

public sealed record ShapeText(string Text, FontStyle Font, HorizontalAlignment HorizontalAlignment,
    VerticalAlignment VerticalAlignment, bool WrapText, double MarginLeft, double MarginTop,
    double MarginRight, double MarginBottom);

public sealed record ShapeAdjustment(double X, double Y);

public sealed record ReportShape(CellAddress Anchor, double OffsetX, double OffsetY, double Width,
    double Height, ShapeKind Kind, ShapeStyle Style, ShapeText? Text, double Rotation, int ZIndex,
    ShapeAdjustment? Adjustment = null);
