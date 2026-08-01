using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ReportEngine.Model;

namespace ReportEngine.Excel;

public static class ExcelStyleConverter
{
    public static CellStyle Convert(IXLCell cell)
    {
        var style = cell.Style;
        var horizontalAlignment = style.Alignment.Horizontal == XLAlignmentHorizontalValues.General
            ? ResolveGeneralAlignment(cell)
            : ToHorizontalAlignment(style.Alignment.Horizontal);
        return new(new FontStyle(
            style.Font.FontName,
            style.Font.FontSize,
            style.Font.Bold,
            style.Font.Italic,
            style.Font.Underline != XLFontUnderlineValues.None,
            ToColor(style.Font.FontColor)),
            ToBackground(style.Fill),
            ToBorder(style.Border),
            horizontalAlignment,
            ToVerticalAlignment(style.Alignment.Vertical),
            style.Alignment.WrapText,
            style.Alignment.ShrinkToFit);
    }

    private static HorizontalAlignment ResolveGeneralAlignment(IXLCell cell) =>
        cell.DataType is XLDataType.Number or XLDataType.DateTime or XLDataType.TimeSpan
            ? HorizontalAlignment.Right
            : cell.DataType is XLDataType.Boolean
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;

    private static ReportColor? ToBackground(IXLFill fill) =>
        fill.PatternType == XLFillPatternValues.None ? null : ToColor(fill.BackgroundColor);

    private static BorderStyle? ToBorder(IXLBorder border)
    {
        var left = ToBorderSide(border.LeftBorder, border.LeftBorderColor);
        var top = ToBorderSide(border.TopBorder, border.TopBorderColor);
        var right = ToBorderSide(border.RightBorder, border.RightBorderColor);
        var bottom = ToBorderSide(border.BottomBorder, border.BottomBorderColor);
        return left is null && top is null && right is null && bottom is null
            ? null
            : new BorderStyle(left, top, right, bottom);
    }

    private static BorderSide? ToBorderSide(XLBorderStyleValues style, XLColor color) =>
        style == XLBorderStyleValues.None ? null : new BorderSide(ToBorderWidth(style), ToColor(color));

    private static double ToBorderWidth(XLBorderStyleValues style) => style switch
    {
        XLBorderStyleValues.Thick => 2,
        XLBorderStyleValues.Medium or XLBorderStyleValues.MediumDashed or
            XLBorderStyleValues.MediumDashDot or XLBorderStyleValues.MediumDashDotDot => 1,
        XLBorderStyleValues.Double => 0.75,
        _ => 0.5
    };

    private static ReportColor? ToColor(XLColor color) =>
        color.HasValue ? new ReportColor(color.Color.R, color.Color.G, color.Color.B, color.Color.A) : null;

    private static HorizontalAlignment ToHorizontalAlignment(XLAlignmentHorizontalValues value) => value switch
    {
        XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous or
            XLAlignmentHorizontalValues.Distributed => HorizontalAlignment.Center,
        XLAlignmentHorizontalValues.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left
    };

    private static VerticalAlignment ToVerticalAlignment(XLAlignmentVerticalValues value) => value switch
    {
        XLAlignmentVerticalValues.Center or XLAlignmentVerticalValues.Distributed => VerticalAlignment.Center,
        XLAlignmentVerticalValues.Bottom => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Top
    };
}
