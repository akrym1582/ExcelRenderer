using System.Collections.Generic;
using ClosedXML.Excel;

namespace ExcelRenderer.Mapping;

/// <summary>A validated node with evaluated child blocks or cell values for every copy.</summary>
internal sealed class ExpansionNode
{
    /// <summary>Initializes a new instance of the <see cref="ExpansionNode"/> class.</summary>
    /// <param name="template">The template.</param>
    internal ExpansionNode(TemplateNode template)
    {
        Template = template;
    }

    /// <summary>Gets the original template node.</summary>
    internal TemplateNode Template { get; }

    /// <summary>Gets the evaluated copies of an explicit block.</summary>
    internal List<List<ExpansionNode>> Blocks { get; } = new List<List<ExpansionNode>>();

    /// <summary>Gets the evaluated copies of a single row.</summary>
    internal List<List<XLCellValue>> Rows { get; } = new List<List<XLCellValue>>();
}
