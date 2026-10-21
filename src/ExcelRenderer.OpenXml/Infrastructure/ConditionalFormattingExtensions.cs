using System;
using System.Linq;
using System.Xml.Linq;

namespace ExcelRenderer.OpenXml.Infrastructure;

/// <summary>Recognizes conditional-format containers independently of their rendering support.</summary>
internal static class ConditionalFormattingExtensions
{
    /// <summary>Checks extension namespace and registered container identifiers.</summary>
    /// <param name="root">Saved extension-list subtree.</param>
    /// <returns>Whether the payload is confined to conditional-format metadata.</returns>
    internal static bool IsKnown(XElement root)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace x14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace xm = "http://schemas.microsoft.com/office/excel/2006/main";
        return root.Name == main + "extLst" && root.Elements().Any() && root.Elements().All(extension => extension.Name == main + "ext" && extension.Elements().Any() && extension.Elements().All(payload =>
            (payload.Name == x14 + "id" && Guid.TryParse((string?)extension.Attribute("uri"), out var linkUri) && linkUri == new Guid("B025F937-C7B1-47D3-B67F-A62EFF666E3E")) || (payload.Name == x14 + "conditionalFormattings" && Guid.TryParse((string?)extension.Attribute("uri"), out var rulesUri) && rulesUri == new Guid("78C0D931-6437-407D-A8EE-F0AAD7539E65") && payload.Elements().All(format =>
                format.Name == x14 + "conditionalFormatting" && format.Element(xm + "sqref") is not null && format.Elements().All(child => child.Name == xm + "sqref" ||
                    child.Name == x14 + "cfRule")))));
    }
}
