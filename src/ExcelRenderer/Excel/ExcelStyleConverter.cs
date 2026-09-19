using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

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
            ToColor(style.Font.FontColor, cell.Worksheet.Workbook.Theme)),
            ToBackground(style.Fill, cell.Worksheet.Workbook.Theme),
            ToBorder(style.Border, cell.Worksheet.Workbook.Theme),
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
        style == XLBorderStyleValues.None ? null : new BorderSide(ToBorderWidth(style), ToColor(color, theme), ToLineStyle(style));

    private static double ToBorderWidth(XLBorderStyleValues style) => style switch
    {
        XLBorderStyleValues.Thick => 2,
        XLBorderStyleValues.Medium or XLBorderStyleValues.MediumDashed or
            XLBorderStyleValues.MediumDashDot or XLBorderStyleValues.MediumDashDotDot => 1,
        XLBorderStyleValues.Double => 0.75,
        _ => 0.5
    };

    private static BorderLineStyle ToLineStyle(XLBorderStyleValues style) => style switch
    {
        XLBorderStyleValues.Dotted => BorderLineStyle.Dotted,
        XLBorderStyleValues.Dashed or XLBorderStyleValues.MediumDashed => BorderLineStyle.Dashed,
        XLBorderStyleValues.DashDot or XLBorderStyleValues.MediumDashDot => BorderLineStyle.DashDot,
        XLBorderStyleValues.DashDotDot or XLBorderStyleValues.MediumDashDotDot => BorderLineStyle.DashDotDot,
        _ => BorderLineStyle.Solid
    };

    private static ReportColor? ToColor(XLColor color, IXLTheme theme)
    {
        if (!color.HasValue) return null;
        // Indexed 64/65 are automatic foreground/background, not palette entries.
        if (color.ColorType == XLColorType.Indexed && color.Indexed >= 64)
            return color.Indexed == 65 ? new(255, 255, 255) : new(0, 0, 0);
        var resolved = color.ColorType == XLColorType.Theme
            ? theme.ResolveThemeColor(color.ThemeColor).Color
            : color.Color;
        if (color.ColorType != XLColorType.Theme || color.ThemeTint == 0)
            return new(resolved.R, resolved.G, resolved.B, resolved.A);

        // SpreadsheetML tint modifies HSL luminance, preserving hue/saturation.
        var luminance = (double)resolved.GetBrightness();
        var tint = color.ThemeTint;
        luminance = tint < 0 ? luminance * (1 + tint) : luminance * (1 - tint) + tint;
        var saturation = resolved.GetSaturation();
        var chroma = (1 - Math.Abs(2 * luminance - 1)) * saturation;
        var hue = resolved.GetHue() / 60d;
        var x = chroma * (1 - Math.Abs(hue % 2 - 1));
        var (r, g, b) = hue switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        var m = luminance - chroma / 2;
        byte Channel(double value) => (byte)Math.Round((value + m) * 255);
        return new(Channel(r), Channel(g), Channel(b), resolved.A);
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
