using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

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
    }

    /// <summary>Gets the Text metadata.</summary>
    public string Text { get; }

    /// <summary>Gets the Properties metadata.</summary>
    public ElementInfo? Properties { get; }
}
