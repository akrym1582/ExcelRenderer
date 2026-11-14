using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ExcelRenderer.OpenXml.Writing;

/// <summary>Applies isolated font color changes to cell styles.</summary>
public static partial class OpenXmlWorkbookWriter
{
    private static void ApplyFontColor(XElement cell, XElement row, SheetSource source, int column, string color, XDocument styles, Dictionary<(int Style, int Format), int> edits)
    {
        if (!Regex.IsMatch(color, @"^[0-9A-Fa-f]{6}\z"))
        {
            throw new ArgumentException("Font color requires six RGB hexadecimal digits.", nameof(color));
        }

        var original = (int?)cell.Attribute("s") ?? ((string?)row.Attribute("customFormat") is "1" or "true" ? (int?)row.Attribute("s") : null) ?? source.ColumnStyle(column);
        var key = (original, -2 - int.Parse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (!edits.TryGetValue(key, out var index))
        {
            var formats = styles.Root!.Element(Ns + "cellXfs") ?? throw new InvalidDataException("Missing cell formats.");
            var fonts = styles.Root.Element(Ns + "fonts") ?? throw new InvalidDataException("Missing fonts.");
            var format = new XElement(formats.Elements().ElementAt(original));
            var fontId = (int?)format.Attribute("fontId");
            if (fontId is null && (int?)format.Attribute("xfId") is int parentIndex)
            {
                var parent = styles.Root.Element(Ns + "cellStyleXfs")?.Elements(Ns + "xf").ElementAtOrDefault(parentIndex);
                fontId = (int?)parent?.Attribute("fontId");
            }

            var font = new XElement(fonts.Elements(Ns + "font").ElementAt(fontId ?? 0));
            font.Elements(Ns + "color").Remove();
            var coloredFont = new DocumentFormat.OpenXml.Spreadsheet.Font(font.ToString());
            coloredFont.Color = new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = "FF" + color.ToUpperInvariant() };
            font = XElement.Parse(coloredFont.OuterXml);
            var fontIndex = fonts.Elements().ToList().FindIndex(existing => XNode.DeepEquals(existing, font));
            if (fontIndex < 0)
            {
                fontIndex = fonts.Elements().Count();
                fonts.Add(font);
                fonts.SetAttributeValue("count", fontIndex + 1);
            }

            format.SetAttributeValue("fontId", fontIndex);
            format.SetAttributeValue("applyFont", 1);
            index = formats.Elements().Count();
            formats.Add(format);
            formats.SetAttributeValue("count", index + 1);
            edits.Add(key, index);
        }

        cell.SetAttributeValue("s", index);
    }
}
