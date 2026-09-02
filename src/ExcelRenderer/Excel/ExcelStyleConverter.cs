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

        var (red, green, blue) = ApplyTint(resolvedColor.R, resolvedColor.G, resolvedColor.B, tint);
        return new ReportColor(red, green, blue, resolvedColor.A);
    }

    private static (byte Red, byte Green, byte Blue) ApplyTint(byte red, byte green, byte blue, double tint)
    {
        if (tint == 0)
            return (red, green, blue);

        var normalizedRed = red / 255d;
        var normalizedGreen = green / 255d;
        var normalizedBlue = blue / 255d;
        var maximum = Math.Max(normalizedRed, Math.Max(normalizedGreen, normalizedBlue));
        var minimum = Math.Min(normalizedRed, Math.Min(normalizedGreen, normalizedBlue));
        var luminance = (maximum + minimum) / 2;
        var saturation = 0d;
        var hue = 0d;

        if (maximum != minimum)
        {
            var difference = maximum - minimum;
            saturation = luminance > 0.5
                ? difference / (2 - maximum - minimum)
                : difference / (maximum + minimum);

            hue = maximum == normalizedRed
                ? (normalizedGreen - normalizedBlue) / difference + (normalizedGreen < normalizedBlue ? 6 : 0)
                : maximum == normalizedGreen
                    ? (normalizedBlue - normalizedRed) / difference + 2
                    : (normalizedRed - normalizedGreen) / difference + 4;
            hue /= 6;
        }

        luminance = tint < 0
            ? luminance * (1 + tint)
            : luminance * (1 - tint) + tint;
        luminance = Math.Max(0, Math.Min(1, luminance));

        if (saturation == 0)
        {
            var component = ToByte(luminance);
            return (component, component, component);
        }

        var second = luminance < 0.5
            ? luminance * (1 + saturation)
            : luminance + saturation - luminance * saturation;
        var first = 2 * luminance - second;
        return (
            ToByte(HueToRgb(first, second, hue + 1d / 3)),
            ToByte(HueToRgb(first, second, hue)),
            ToByte(HueToRgb(first, second, hue - 1d / 3)));
    }

    private static double HueToRgb(double first, double second, double hue)
    {
        if (hue < 0) hue += 1;
        if (hue > 1) hue -= 1;
        if (hue < 1d / 6) return first + (second - first) * 6 * hue;
        if (hue < 1d / 2) return second;
        if (hue < 2d / 3) return first + (second - first) * (2d / 3 - hue) * 6;
        return first;
    }

    private static byte ToByte(double component) =>
        (byte)Math.Round(Math.Max(0, Math.Min(255, component * 255)));

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
