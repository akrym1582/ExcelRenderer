using System;
using System.IO;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable source font properties; null means unspecified and can inherit from a base font.</summary>
public sealed class FontInfo
{
    private FontInfo(string? name, double? sizePt, bool? bold, bool? italic, bool? strikethrough, string? underline, string? verticalAlignment, string? scheme, ElementInfo? color)
    {
        Name = name;
        SizePt = sizePt;
        Bold = bold;
        Italic = italic;
        Strikethrough = strikethrough;
        Underline = underline;
        VerticalAlignment = verticalAlignment;
        Scheme = scheme;
        Color = color;
    }

    /// <summary>Gets the source font name, without fallback or substitution.</summary>
    public string? Name { get; }

    /// <summary>Gets the font size in points, or null when not specified.</summary>
    public double? SizePt { get; }

    /// <summary>Gets a value indicating whether bold is requested, or null when not specified.</summary>
    public bool? Bold { get; }

    /// <summary>Gets a value indicating whether italic is requested, or null when not specified.</summary>
    public bool? Italic { get; }

    /// <summary>Gets a value indicating whether strikethrough is requested, or null when not specified.</summary>
    public bool? Strikethrough { get; }

    /// <summary>Gets the source underline token, including explicit none, or null when not specified.</summary>
    public string? Underline { get; }

    /// <summary>Gets the baseline/superscript/subscript source token, or null when not specified.</summary>
    public string? VerticalAlignment { get; }

    /// <summary>Gets the theme major/minor/none token, without resolving a font fallback.</summary>
    public string? Scheme { get; }

    /// <summary>Gets the source color and optional resolvedRgb, retaining theme/indexed/auto and tint.</summary>
    public ElementInfo? Color { get; }

    /// <summary>Overlays only explicitly supplied properties, preserving false and underline none.</summary>
    /// <param name="overrides">Partial run properties; null leaves this font unchanged.</param>
    /// <returns>A font snapshot independent of any reader lifetime. Unspecified base properties remain null.</returns>
    public FontInfo WithOverrides(FontInfo? overrides) => overrides == null ? this : new FontInfo(
        overrides.Name ?? Name,
        overrides.SizePt ?? SizePt,
        overrides.Bold ?? Bold,
        overrides.Italic ?? Italic,
        overrides.Strikethrough ?? Strikethrough,
        overrides.Underline ?? Underline,
        overrides.VerticalAlignment ?? VerticalAlignment,
        overrides.Scheme ?? Scheme,
        overrides.Color ?? Color);

    /// <summary>Reads saved cell-font or partial run-font properties without interpreting drawing policy.</summary>
    /// <param name="element">Independent source metadata.</param>
    /// <returns>Nullable source properties.</returns>
    internal static FontInfo FromElement(ElementInfo element)
    {
        string? name = null;
        double? size = null;
        bool? bold = null;
        bool? italic = null;
        bool? strike = null;
        string? underline = null;
        string? vertical = null;
        string? scheme = null;
        ElementInfo? color = null;
        foreach (var child in element.Children)
        {
            child.Attributes.TryGetValue("val", out var value);
            switch (child.Name)
            {
                case "name":
                case "rFont": name = value; break;
                case "sz": size = value == null ? (double?)null : XmlValues.Number(value); break;
                case "b": bold = Boolean(value); break;
                case "i": italic = Boolean(value); break;
                case "strike": strike = Boolean(value); break;
                case "u": underline = value ?? "single"; break;
                case "vertAlign": vertical = value; break;
                case "scheme": scheme = value; break;
                case "color": color = child; break;
            }
        }

        return new FontInfo(name, size, bold, italic, strike, underline, vertical, scheme, color);
    }

    private static bool Boolean(string? value) => value == null || value == "1" || value == "true" ? true : value == "0" || value == "false" ? false : throw new InvalidDataException("Invalid font boolean value.");
}
