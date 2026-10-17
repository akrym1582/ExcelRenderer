using System.Collections.Generic;

namespace ExcelRenderer.OpenXml.Model.Workbook;

/// <summary>Immutable ElementInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class ElementInfo
{
    /// <summary>Initializes a new instance of the <see cref="ElementInfo"/> class.</summary>
    /// <param name="name">The name value.</param>
    /// <param name="text">The text value.</param>
    /// <param name="attributes">The attributes value.</param>
    /// <param name="children">The children value.</param>
    internal ElementInfo(string name, string text, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<ElementInfo> children)
    {
        Name = name;
        Text = text;
        Attributes = attributes;
        Children = children;
    }

    /// <summary>Gets the Name metadata.</summary>
    public string Name { get; }

    /// <summary>Gets the text for a leaf metadata element, without interpreting header/footer control codes.</summary>
    public string Text { get; }

    /// <summary>Gets the immutable original XML attributes plus resolvedRgb on colors that can be resolved.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>Gets the immutable child metadata entries; this snapshot holds no SDK elements or streams.</summary>
    public IReadOnlyList<ElementInfo> Children { get; }
}
