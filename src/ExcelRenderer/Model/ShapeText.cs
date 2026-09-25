namespace ExcelRenderer.Model;

/// <summary>
/// 図形内の文字列、フォント、配置、折り返し、および各辺の余白を表します。
/// </summary>
public sealed record ShapeText(string Text, FontStyle Font, HorizontalAlignment HorizontalAlignment,
    VerticalAlignment VerticalAlignment, bool WrapText, double MarginLeft, double MarginTop,
    double MarginRight, double MarginBottom);
