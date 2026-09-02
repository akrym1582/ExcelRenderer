using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

public static class ExcelStyleConverter
{
    public static CellStyle Convert(IXLCell cell)
    {
        var style = cell.Style;
        var theme = cell.Worksheet.Workbook.Theme;
        var horizontalAlignment = style.Alignment.Horizontal == XLAlignmentHorizontalValues.General
            ? ResolveGeneralAlignment(cell)
            : ToHorizontalAlignment(style.Alignment.Horizontal);
        return new(new FontStyle(
            style.Font.FontName,
            style.Font.FontSize,
            style.Font.Bold,
            style.Font.Italic,
            style.Font.Underline != XLFontUnderlineValues.None,
            ToColor(style.Font.FontColor, theme)),
            ToBackground(style.Fill, theme),
            ToBorder(style.Border, theme),
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

    private static ReportColor? ToBackground(IXLFill fill, IXLTheme theme) =>
        fill.PatternType == XLFillPatternValues.None ? null : ToColor(fill.BackgroundColor, theme);

    private static BorderStyle? ToBorder(IXLBorder border, IXLTheme theme)
    {
        var left = ToBorderSide(border.LeftBorder, border.LeftBorderColor, theme);
        var top = ToBorderSide(border.TopBorder, border.TopBorderColor, theme);
        var right = ToBorderSide(border.RightBorder, border.RightBorderColor, theme);
        var bottom = ToBorderSide(border.BottomBorder, border.BottomBorderColor, theme);
        return left is null && top is null && right is null && bottom is null
            ? null
            : new BorderStyle(left, top, right, bottom);
    }

    private static BorderSide? ToBorderSide(XLBorderStyleValues style, XLColor color, IXLTheme theme) =>
        style == XLBorderStyleValues.None ? null : new BorderSide(ToBorderWidth(style), ToColor(color, theme));

    private static double ToBorderWidth(XLBorderStyleValues style) => style switch
    {
        XLBorderStyleValues.Thick => 2,
        XLBorderStyleValues.Medium or XLBorderStyleValues.MediumDashed or
            XLBorderStyleValues.MediumDashDot or XLBorderStyleValues.MediumDashDotDot => 1,
        XLBorderStyleValues.Double => 0.75,
        _ => 0.5
    };

    private static ReportColor? ToColor(XLColor color, IXLTheme theme)
    {
        if (!color.HasValue)
            return null;

        var resolvedColor = color.ColorType == XLColorType.Theme
            ? theme.ResolveThemeColor(color.ThemeColor).Color
            : color.Color;
        var tint = color.ColorType == XLColorType.Theme ? color.ThemeTint : 0;

        return new ReportColor(
            ApplyTint(resolvedColor.R, tint),
            ApplyTint(resolvedColor.G, tint),
            ApplyTint(resolvedColor.B, tint),
            resolvedColor.A);
    }

    private static byte ApplyTint(byte component, double tint)
    {
        var value = tint < 0
            ? component * (1 + tint)
            : component + (255 - component) * tint;
        return (byte)Math.Round(Math.Max(0, Math.Min(255, value)));
    }

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
