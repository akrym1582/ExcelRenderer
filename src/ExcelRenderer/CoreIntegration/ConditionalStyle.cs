using System.Globalization;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Model;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Retains explicit differential properties independently of ordinary style defaults.</summary>
internal sealed class ConditionalStyle
{
    private readonly Dictionary<string, ElementInfo> properties = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="ConditionalStyle"/> class.</summary>
    /// <param name="source">Resolved saved differential.</param>
    internal ConditionalStyle(ElementInfo? source)
    {
        foreach (var child in source?.Children ?? [])
        {
            switch (child.Name)
            {
                case "font":
                    foreach (var property in child.Children)
                    {
                        if (property.Name is "b" or "i" or "u" or "color")
                        {
                            properties[property.Name] = property;
                        }
                        else
                        {
                            HasUnsupportedProperties = true;
                        }
                    }

                    break;
                case "fill": properties["fill"] = child; break;
                case "border":
                    foreach (var edge in child.Children)
                    {
                        if (edge.Name is "left" or "right" or "top" or "bottom")
                        {
                            properties[edge.Name] = edge;
                        }
                        else
                        {
                            HasUnsupportedProperties = true;
                        }
                    }

                    HasUnsupportedProperties |= child.Attributes.Any(attribute => attribute.Value is not "0" and not "false");
                    break;
                default: HasUnsupportedProperties = true; break;
            }
        }

        HasUnsupportedProperties |= properties.Any(property => !Supported(property.Key, property.Value));
    }

    /// <summary>Gets a value indicating whether some source properties cannot be rendered.</summary>
    internal bool HasUnsupportedProperties { get; }

    /// <summary>Gets a value indicating whether a matching rule needs a property composition map.</summary>
    internal bool HasProperties => properties.Count > 0;

    /// <summary>Gets a value indicating whether the differential sets or clears a cell edge.</summary>
    internal bool HasBorders => properties.ContainsKey("left") || properties.ContainsKey("right") || properties.ContainsKey("top") || properties.ContainsKey("bottom");

    /// <summary>Applies explicit supported properties to a normal cell appearance.</summary>
    /// <param name="style">Normal style.</param>
    /// <param name="properties">Winning explicit properties.</param>
    /// <param name="background">Optional winning dynamic background.</param>
    /// <returns>The effective style.</returns>
    internal static CellStyle Apply(CellStyle style, IReadOnlyDictionary<string, ElementInfo> properties, ReportColor? background = null)
    {
        var border = style.Border ?? new BorderStyle();
        var result = style with
        {
            Font = ApplyFont(style.Font, properties),
            Background = background ?? (properties.TryGetValue("fill", out var fill) && Supported("fill", fill)
                ? Attribute(Child(fill, "patternFill"), "patternType") != "none" ? FillColor(fill) : null : style.Background),
            Border = border with
            {
                Left = Edge("left", border.Left),
                Top = Edge("top", border.Top),
                Right = Edge("right", border.Right),
                Bottom = Edge("bottom", border.Bottom),
            },
        };
        return result with { Border = result.Border == new BorderStyle() ? null : result.Border };

        BorderSide? Edge(string name, BorderSide? original) => properties.TryGetValue(name, out var value) && Supported(name, value)
            ? OpenXmlStyleConverter.Border(value) is { } side ? side with { Color = ResolvedColor(Child(value, "color")) } : null : original;
    }

    /// <summary>Applies the same differential font to normal text and every rich-text run.</summary>
    /// <param name="font">Original effective font.</param>
    /// <param name="properties">Winning explicit properties.</param>
    /// <returns>The effective font.</returns>
    internal static FontStyle ApplyFont(FontStyle font, IReadOnlyDictionary<string, ElementInfo> properties) => font with
    {
        Bold = properties.TryGetValue("b", out var bold) && Supported("b", bold) ? Attribute(bold, "val") is not "0" and not "false" : font.Bold,
        Italic = properties.TryGetValue("i", out var italic) && Supported("i", italic) ? Attribute(italic, "val") is not "0" and not "false" : font.Italic,
        Underline = properties.TryGetValue("u", out var underline) && Supported("u", underline) ? Attribute(underline, "val") != "none" : font.Underline,
        Color = properties.TryGetValue("color", out var color) && Supported("color", color) ? ResolvedColor(color) : font.Color,
    };

    /// <summary>Reads Reader-resolved RGB without creating rendering metadata in Reader.</summary>
    /// <param name="color">Saved resolved color.</param>
    /// <returns>The typed RGB color when available.</returns>
    internal static ReportColor? ResolvedColor(ElementInfo? color)
    {
        if (Attribute(color, "resolvedRgb") is { } rgb && uint.TryParse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return new((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        return Attribute(color, "auto") is "1" or "true" || Attribute(color, "indexed") is "64" or "65" ? Color(color) : null;
    }

    /// <summary>Combines matched rules in priority order, reserving even unsupported properties against lower priorities.</summary>
    /// <param name="higher">Properties already claimed by higher priorities.</param>
    /// <param name="fillClaimed">Whether a higher dynamic background already owns the fill.</param>
    internal void AddTo(Dictionary<string, ElementInfo> higher, bool fillClaimed = false)
    {
        foreach (var property in properties)
        {
            if (!higher.ContainsKey(property.Key) && !(fillClaimed && property.Key == "fill"))
            {
                higher.Add(property.Key, property.Value);
            }
        }
    }

    private static ReportColor? FillColor(ElementInfo fill)
    {
        var pattern = Child(fill, "patternFill");
        var foreground = Child(pattern, "fgColor");
        var background = Child(pattern, "bgColor");

        // Differential backgrounds can be saved as bgColor with an automatic
        // foreground, or without a patternType; ordinary XF fill defaults do not apply.
        return foreground is null || Attribute(foreground, "auto") is "1" or "true"
            ? ResolvedColor(background) ?? ResolvedColor(foreground) : ResolvedColor(foreground) ?? ResolvedColor(background);
    }

    private static bool Supported(string key, ElementInfo value) => key switch
    {
        "b" or "i" => Attribute(value, "val") is null or "0" or "1" or "true" or "false",
        "u" => Attribute(value, "val") is null or "single" or "none",
        "color" => ResolvedColor(value) is not null,
        "fill" => value.Children.All(child => child.Name == "patternFill") && Child(value, "patternFill") is { } pattern &&
            (Attribute(pattern, "patternType") == "none" || (Attribute(pattern, "patternType") is null or "solid" &&
                (FillColor(value) is not null || (Attribute(pattern, "patternType") is null && pattern.Children.Count == 0)))),
        _ => (Attribute(value, "style") is null or "none" or "thin" or "medium" or "thick" or "dashed" or "dotted" or "double" or "hair" or "mediumDashed" or "dashDot" or "mediumDashDot" or "dashDotDot" or "mediumDashDotDot" or "slantDashDot") &&
            (Child(value, "color") is null || ResolvedColor(Child(value, "color")) is not null),
    };
}
