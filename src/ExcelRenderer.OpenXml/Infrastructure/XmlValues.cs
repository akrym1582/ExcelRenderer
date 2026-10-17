using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Model.Workbook;

namespace ExcelRenderer.OpenXml.Infrastructure;

/// <summary>Provides XmlValues metadata.</summary>
internal static class XmlValues
{
    /// <summary>Provides "http://schemas.openxmlformats.org/spreadsheetml/2006/main" metadata.</summary>
    internal static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Provides Attr metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="e">The e value.</param>
    /// <param name="name">The name value.</param>
    internal static string? Attr(XElement e, string name) => (string?)e.Attribute(name);

    /// <summary>Provides Int metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="e">The e value.</param>
    /// <param name="name">The name value.</param>
    internal static int? Int(XElement e, string name)
    {
        var s = Attr(e, name);
        if (s == null)
        {
            return null;
        }

        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException("Invalid integer attribute.");
        }

        return value;
    }

    /// <summary>Provides Double metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="e">The e value.</param>
    /// <param name="name">The name value.</param>
    internal static double? Double(XElement e, string name)
    {
        var s = Attr(e, name);
        return s == null ? (double?)null : Number(s);
    }

    /// <summary>Provides Number metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="s">The s value.</param>
    internal static double Number(string s)
    {
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidDataException("Invalid finite numeric value.");
        }

        return value;
    }

    /// <summary>Provides Bool metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="e">The e value.</param>
    /// <param name="name">The name value.</param>
    internal static bool? Bool(XElement e, string name)
    {
        var s = Attr(e, name);
        return s == null ? (bool?)null : s == "1" || s == "true" ? true : s == "0" || s == "false" ? false : throw new InvalidDataException("Invalid boolean attribute.");
    }

    /// <summary>Provides Attributes metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="e">The e value.</param>
    internal static IReadOnlyDictionary<string, string> Attributes(XElement e) => new ReadOnlyDictionary<string, string>(e.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(a => a.Name.ToString(), a => a.Value));

    /// <summary>Provides Info metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="e">The e value.</param>
    internal static ElementInfo Info(XElement e) => new ElementInfo(e.Name.LocalName, e.HasElements ? string.Empty : e.Value, Attributes(e), e.Elements().Select(Info).ToList().AsReadOnly());

    /// <summary>Loads only the current bounded XML entry.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="reader">The reader value.</param>
    internal static XElement Load(OpenXmlReader reader)
    {
        try
        {
            return Project(reader.LoadCurrentElement()!);
        }
        catch (XmlException e)
        {
            throw new InvalidDataException("Invalid XML or MaxCharactersInPart exceeded.", e);
        }
    }

    /// <summary>Opens a part reader and translates eager XML limit checks.</summary>
    /// <param name="part">The package part.</param>
    /// <returns>A caller-disposed streaming reader.</returns>
    internal static OpenXmlReader CreateReader(OpenXmlPart part)
    {
        try
        {
            return OpenXmlReader.Create(part);
        }
        catch (XmlException e)
        {
            throw new InvalidDataException("Invalid XML or MaxCharactersInPart exceeded.", e);
        }
    }

    /// <summary>Advances the SDK reader and translates XML limit failures.</summary>
    /// <param name="reader">The current part reader.</param>
    /// <returns>Whether another XML node exists.</returns>
    internal static bool Read(OpenXmlReader reader)
    {
        try
        {
            return reader.Read();
        }
        catch (XmlException e)
        {
            throw new InvalidDataException("Invalid XML or MaxCharactersInPart exceeded.", e);
        }
    }

    /// <summary>Provides Empty metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="name">The name value.</param>
    internal static XElement Empty(string name) => new XElement(Spreadsheet + name);

    /// <summary>Decodes OOXML UTF-16 escapes once, preserving escaped literals.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="value">The value value.</param>
    internal static string Decode(string value) => value.IndexOf("_x", StringComparison.Ordinal) < 0 ? value : Regex.Replace(value, "_x([0-9a-fA-F]{4})_", m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    private static XElement Project(OpenXmlElement element)
    {
        var result = new XElement(XName.Get(element.LocalName, element.NamespaceUri));
        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.Prefix != "xmlns" && attribute.LocalName != "xmlns")
            {
                result.SetAttributeValue(XName.Get(attribute.LocalName, attribute.NamespaceUri), attribute.Value);
            }
        }

        if (element is OpenXmlLeafTextElement text)
        {
            result.Value = text.Text;
        }
        else if (element.HasChildren)
        {
            foreach (var child in element.ChildElements)
            {
                if (child is not OpenXmlMiscNode)
                {
                    result.Add(Project(child));
                }
            }
        }
        else if (element.InnerText.Length > 0)
        {
            result.Value = element.InnerText;
        }

        return result;
    }
}
