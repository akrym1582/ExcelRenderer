namespace ExcelRenderer.Model;

/// <summary>
/// ShapeText が表すデータと操作を提供します.
/// </summary>
public sealed record ShapeText(string Text, FontStyle Font, HorizontalAlignment HorizontalAlignment,
    VerticalAlignment VerticalAlignment, bool WrapText, double MarginLeft, double MarginTop,
    double MarginRight, double MarginBottom);
