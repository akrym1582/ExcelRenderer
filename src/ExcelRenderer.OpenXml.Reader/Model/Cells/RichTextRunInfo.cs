using System.Collections.Generic;
using ExcelRenderer.OpenXml.Reader.Model.Styles;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;

namespace ExcelRenderer.OpenXml.Reader.Model.Cells;

/// <summary>Immutable RichTextRunInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class RichTextRunInfo
{
    /// <summary>Initializes a new instance of the <see cref="RichTextRunInfo"/> class.</summary>
    /// <param name="text">The text value.</param>
    /// <param name="properties">The properties value.</param>
    internal RichTextRunInfo(string text, ElementInfo? properties)
    {
        Text = text;
        Properties = properties;
        FontProperties = properties == null ? null : FontInfo.FromElement(properties);
    }

    /// <summary>Gets the Text metadata.</summary>
    public string Text { get; }

    /// <summary>Gets the Properties metadata.</summary>
    public ElementInfo? Properties { get; }

    /// <summary>Gets the partial run font; null means inherit the entire cell font.</summary>
    public FontInfo? FontProperties { get; }
}
