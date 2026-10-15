using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace ExcelRenderer.OpenXml;

/// <summary>Provides StyleTable metadata.</summary>
internal sealed class StyleTable
{
    private readonly OpenXmlReaderOptions options;
    private readonly Dictionary<string, List<XElement>> tables = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
    private readonly Dictionary<int, CellStyleInfo> resolved = new Dictionary<int, CellStyleInfo>();
    private readonly Dictionary<int, string> formats = new Dictionary<int, string>();
    private readonly List<string?> themeColors = new List<string?>();

    /// <summary>Initializes a new instance of the <see cref="StyleTable"/> class.</summary>
    /// <param name="workbook">The workbook value.</param>
    /// <param name="options">The options value.</param>
    /// <param name="token">The token value.</param>
    internal StyleTable(WorkbookPart workbook, OpenXmlReaderOptions options, CancellationToken token)
    {
        this.options = options;
        if (workbook.ThemePart != null)
        {
            using var reader = XmlValues.CreateReader(workbook.ThemePart);
            while (XmlValues.Read(reader))
            {
                token.ThrowIfCancellationRequested();
                if (reader.IsStartElement && (reader.LocalName == "clrScheme" || reader.LocalName == "fontScheme"))
                {
                    var e = XmlValues.Load(reader);
                    if (e.Name.LocalName == "clrScheme")
                    {
                        foreach (var colorName in new[] { "lt1", "dk1", "lt2", "dk2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" })
                        {
                            var c = e.Elements().FirstOrDefault(color => color.Name.LocalName == colorName)?.Elements().FirstOrDefault();
                            themeColors.Add(c == null ? null : XmlValues.Attr(c, c.Name.LocalName == "sysClr" ? "lastClr" : "val"));
                        }
                    }
                    else
                    {
                        Theme = XmlValues.Info(e);
                    }
                }
            }
        }

        if (workbook.WorkbookStylesPart != null)
        {
            using var reader = XmlValues.CreateReader(workbook.WorkbookStylesPart);
            string? table = null;
            var depth = -1;
            while (XmlValues.Read(reader))
            {
                token.ThrowIfCancellationRequested();
                if (reader.IsStartElement && reader.LocalName == "indexedColors")
                {
                    table = "indexedColors";
                    depth = reader.Depth;
                    tables[table] = new List<XElement>();
                }
                else if (reader.IsEndElement && reader.LocalName == "indexedColors")
                {
                    table = "colors";
                    depth = 1;
                }
                else if (reader.IsStartElement && reader.Depth == 1)
                {
                    table = reader.LocalName;
                    depth = reader.Depth;
                    tables[table] = new List<XElement>();
                }
                else if (reader.IsStartElement && table != null && reader.Depth == depth + 1)
                {
                    var list = tables[table];
                    if (list.Count >= options.MaxStyleRecords)
                    {
                        throw new InvalidDataException("MaxStyleRecords exceeded.");
                    }

                    list.Add(XmlValues.Load(reader));
                }
            }
        }

        foreach (var e in Table("numFmts"))
        {
            formats[XmlValues.Int(e, "numFmtId") ?? throw new InvalidDataException("Missing number format ID.")] = XmlValues.Attr(e, "formatCode") ?? string.Empty;
        }

        var normal = Table("cellStyles").FirstOrDefault(e => XmlValues.Int(e, "builtinId") == 0);
        var normalXf = normal == null ? null : At("cellStyleXfs", XmlValues.Int(normal, "xfId") ?? 0);
        var fontId = normalXf == null ? 0 : XmlValues.Int(normalXf, "fontId") ?? 0;
        DefaultFont = ColorInfo(At("fonts", fontId));
    }

    /// <summary>Gets the DefaultFont metadata.</summary>
    internal ElementInfo DefaultFont { get; }

    /// <summary>Gets the Theme metadata.</summary>
    internal ElementInfo? Theme { get; }

    /// <summary>Gets the Count metadata.</summary>
    internal int Count => Table("cellXfs").Count;

    /// <summary>Resolves saved XF components with parent fallback.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="index">The index value.</param>
    internal CellStyleInfo Resolve(int index)
    {
        if (resolved.TryGetValue(index, out var result))
        {
            return result;
        }

        var xf = At("cellXfs", index);
        var baseId = XmlValues.Int(xf, "xfId");
        var parent = baseId.HasValue ? At("cellStyleXfs", baseId.Value) : XmlValues.Empty("xf");
        var font = XmlValues.Int(xf, "fontId") ?? XmlValues.Int(parent, "fontId") ?? 0;
        var fill = XmlValues.Int(xf, "fillId") ?? XmlValues.Int(parent, "fillId") ?? 0;
        var border = XmlValues.Int(xf, "borderId") ?? XmlValues.Int(parent, "borderId") ?? 0;
        var number = XmlValues.Int(xf, "numFmtId") ?? XmlValues.Int(parent, "numFmtId") ?? 0;
        var alignment = xf.Element(XmlValues.Spreadsheet + "alignment") ?? parent.Element(XmlValues.Spreadsheet + "alignment");
        var code = formats.TryGetValue(number, out var custom) ? custom : NumberFormats.Builtin(number);
        result = new CellStyleInfo(index, font, fill, border, number, baseId, code, ColorInfo(At("fonts", font)), ColorInfo(At("fills", fill)), ColorInfo(At("borders", border)), alignment == null ? null : XmlValues.Info(alignment), XmlValues.Attributes(xf));
        resolved.Add(index, result);
        return result;
    }

    /// <summary>Preserves color sources and adds resolved RGB where available.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="source">The source value.</param>
    internal ElementInfo ColorInfo(XElement source)
    {
        var e = new XElement(source);
        foreach (var color in e.DescendantsAndSelf().Where(c => c.Name.LocalName == "color" || c.Name.LocalName == "fgColor" || c.Name.LocalName == "bgColor"))
        {
            var rgb = XmlValues.Attr(color, "rgb");
            if (rgb == null && XmlValues.Int(color, "theme") is int theme && theme >= 0 && theme < themeColors.Count)
            {
                rgb = themeColors[theme];
            }

            if (rgb == null && XmlValues.Int(color, "indexed") is int indexed)
            {
                var custom = Table("indexedColors");
                rgb = indexed >= 0 && indexed < custom.Count ? XmlValues.Attr(custom[indexed], "rgb") : Indexed(indexed);
            }

            if (rgb != null && (rgb.Length == 6 || rgb.Length == 8) && uint.TryParse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                var tint = XmlValues.Double(color, "tint") ?? 0;
                if (tint < -1 || tint > 1)
                {
                    throw new InvalidDataException("Invalid color tint.");
                }

                color.SetAttributeValue("resolvedRgb", Tint(value & 0xffffff, tint));
            }
            else
            {
                options.DiagnosticSink?.Invoke(new ReaderDiagnostic(ReaderDiagnosticCodes.UnresolvedColor, "Warning", null, null, "Color cannot be resolved; original attributes are retained."));
            }
        }

        return XmlValues.Info(e);
    }

    private static string? Indexed(int index)
    {
        var palette = new[]
        {
            "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
            "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
            "800000", "008000", "000080", "808000", "800080", "008080", "C0C0C0", "808080",
            "9999FF", "993366", "FFFFCC", "CCFFFF", "660066", "FF8080", "0066CC", "CCCCFF",
            "000080", "FF00FF", "FFFF00", "00FFFF", "800080", "800000", "008080", "0000FF",
            "00CCFF", "CCFFFF", "CCFFCC", "FFFF99", "99CCFF", "FF99CC", "CC99FF", "FFCC99",
            "3366FF", "33CCCC", "99CC00", "FFCC00", "FF9900", "FF6600", "666699", "969696",
            "003366", "339966", "003300", "333300", "993300", "993366", "333399", "333333",
        };
        return index >= 0 && index < palette.Length ? palette[index] : null;
    }

    private static string Tint(uint rgb, double tint)
    {
        var r = ((rgb >> 16) & 255) / 255.0;
        var g = ((rgb >> 8) & 255) / 255.0;
        var b = (rgb & 255) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var light = (max + min) / 2;
        var delta = max - min;
        var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs((2 * light) - 1));
        var hue = delta == 0 ? 0 : max == r ? ((g - b) / delta) % 6 : max == g ? ((b - r) / delta) + 2 : ((r - g) / delta) + 4;
        hue = (hue + 6) % 6;
        light = Math.Round(light * 240) / 240;
        light = tint < 0 ? light * (1 + tint) : (light * (1 - tint)) + tint;
        var chroma = (1 - Math.Abs((2 * light) - 1)) * saturation;
        var x = chroma * (1 - Math.Abs((hue % 2) - 1));
        var m = light - (chroma / 2);
        var components = hue < 1 ? new[] { chroma, x, 0.0 } : hue < 2 ? new[] { x, chroma, 0.0 } : hue < 3 ? new[] { 0.0, chroma, x } : hue < 4 ? new[] { 0.0, x, chroma } : hue < 5 ? new[] { x, 0.0, chroma } : new[] { chroma, 0.0, x };
        return string.Concat(components.Select(c => ((int)Math.Round((c + m) * 255, MidpointRounding.AwayFromZero)).ToString("X2", CultureInfo.InvariantCulture)));
    }

    private List<XElement> Table(string name) => tables.TryGetValue(name, out var list) ? list : new List<XElement>();

    private XElement At(string table, int index)
    {
        var values = Table(table);
        if (index == 0 && values.Count == 0 && !tables.ContainsKey(table))
        {
            return XmlValues.Empty(table == "fonts" ? "font" : table == "fills" ? "fill" : table == "borders" ? "border" : "xf");
        }

        if (index < 0 || index >= values.Count)
        {
            throw new InvalidDataException("Invalid style table reference.");
        }

        return values[index];
    }
}
