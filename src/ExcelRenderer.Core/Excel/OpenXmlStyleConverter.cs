using System.Globalization;
using ExcelRenderer.Core.Model;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Model.Styles;
using ExcelRenderer.OpenXml.Model.Workbook;

namespace ExcelRenderer.Core.Excel;

/// <summary>Converts source snapshots to the shared rendering style.</summary>
internal static class OpenXmlStyleConverter
{
    /// <summary>Reads the original cell appearance.</summary>
    /// <param name="style">The resolved source style.</param>
    /// <param name="type">Stored value category for General alignment.</param>
    /// <returns>The neutral style.</returns>
    internal static CellStyle Convert(CellStyleInfo style, CellValueType type)
    {
        var font = Font(style.FontProperties);
        var pattern = Child(style.Fill, "patternFill");
        var background = Attribute(pattern, "patternType") is null or "none" ? null : Color(Child(pattern, "fgColor"));
        var left = Border(Child(style.Border, "left"));
        var top = Border(Child(style.Border, "top"));
        var right = Border(Child(style.Border, "right"));
        var bottom = Border(Child(style.Border, "bottom"));
        var alignment = style.Alignment;
        var horizontal = Attribute(alignment, "horizontal") switch
        {
            "right" => HorizontalAlignment.Right,
            "center" or "centerContinuous" or "distributed" => HorizontalAlignment.Center,
            null or "general" => type is CellValueType.Number or CellValueType.IsoDate ? HorizontalAlignment.Right :
                type == CellValueType.Boolean ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            _ => HorizontalAlignment.Left,
        };
        var vertical = Attribute(alignment, "vertical") switch
        {
            "center" or "distributed" => VerticalAlignment.Center,
            null or "bottom" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Top,
        };
        var rotation = (int)Number(alignment, "textRotation", 0);
        return new(font, background, left is null && top is null && right is null && bottom is null ? null : new(left, top, right, bottom), horizontal, vertical, Boolean(alignment, "wrapText"), Boolean(alignment, "shrinkToFit"))
        {
            Indent = Math.Max(0, (int)(Number(alignment, "indent", 0) + Number(alignment, "relativeIndent", 0))),
            TextRotation = rotation == 255 ? 0 : rotation > 90 ? 90 - rotation : rotation,
            TopToBottom = rotation == 255,
        };
    }

    /// <summary>Converts resolved source font properties.</summary>
    /// <param name="font">A cell font or composed run font.</param>
    /// <returns>The original font appearance.</returns>
    internal static FontStyle Font(FontInfo font) => new(font.Name ?? "Calibri", font.SizePt ?? 11, font.Underline is not null and not "none", Color(font.Color) ?? new ReportColor(0, 0, 0))
    {
        Bold = font.Bold ?? false,
        Italic = font.Italic ?? false,
    };

    /// <summary>Reads a resolved RGB color.</summary>
    /// <param name="element">The source color element.</param>
    /// <returns>The resolved color, or null for an omitted color.</returns>
    internal static ReportColor? Color(ElementInfo? element)
    {
        var text = Attribute(element, "baseRgb") ?? Attribute(element, "rgb") ?? Attribute(element, "resolvedRgb");
        if (text is null)
        {
            return Attribute(element, "indexed") == "65" ? new(255, 255, 255) : Attribute(element, "auto") is "1" or "true" || Attribute(element, "indexed") == "64" ? new(0, 0, 0) : null;
        }

        var rgb = uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var (red, green, blue) = ColorConverter.ApplyTint((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, Attribute(element, "theme") is not null ? Number(element, "tint", 0) : 0);
        return new(red, green, blue, Attribute(element, "resolvedAlpha") is { } alpha ? byte.Parse(alpha, CultureInfo.InvariantCulture) : text.Length == 8 ? (byte)(rgb >> 24) : (byte)255);
    }

    /// <summary>Finds a source metadata child.</summary>
    /// <param name="element">The parent.</param>
    /// <param name="name">Local child name.</param>
    /// <returns>The first matching child.</returns>
    internal static ElementInfo? Child(ElementInfo? element, string name) => element?.Children.FirstOrDefault(child => child.Name == name);

    /// <summary>Reads a source attribute.</summary>
    /// <param name="element">The source element.</param>
    /// <param name="name">Attribute name.</param>
    /// <returns>The saved value.</returns>
    internal static string? Attribute(ElementInfo? element, string name) => element?.Attributes.GetValueOrDefault(name);

    /// <summary>Reads a numeric source attribute.</summary>
    /// <param name="element">The source element.</param>
    /// <param name="name">Attribute name.</param>
    /// <param name="fallback">Value for an omitted attribute.</param>
    /// <returns>The saved number.</returns>
    internal static double Number(ElementInfo? element, string name, double fallback) => Attribute(element, name) is { } value ? OpenXml.Infrastructure.XmlValues.Number(value) : fallback;

    /// <summary>Reads a boolean source attribute.</summary>
    /// <param name="element">The source element.</param>
    /// <param name="name">Attribute name.</param>
    /// <returns>Whether the saved flag is true.</returns>
    internal static bool Boolean(ElementInfo? element, string name) => Attribute(element, name) is "1" or "true";

    private static BorderSide? Border(ElementInfo? edge)
    {
        var style = Attribute(edge, "style");
        if (style is null or "none")
        {
            return null;
        }

        var width = style switch
        {
            "thick" => 2,
            "medium" or "mediumDashed" or "mediumDashDot" or "mediumDashDotDot" => 1,
            "double" => 0.75,
            "hair" => 0.25,
            _ => 0.5,
        };
        var line = style switch
        {
            "dotted" => BorderLineStyle.Dotted,
            "dashed" or "mediumDashed" => BorderLineStyle.Dashed,
            "dashDot" or "mediumDashDot" => BorderLineStyle.DashDot,
            "dashDotDot" or "mediumDashDotDot" => BorderLineStyle.DashDotDot,
            "double" => BorderLineStyle.Double,
            "hair" => BorderLineStyle.Hair,
            "slantDashDot" => BorderLineStyle.SlantDashDot,
            _ => BorderLineStyle.Solid,
        };
        return new(width, Color(Child(edge, "color")), line);
    }
}
