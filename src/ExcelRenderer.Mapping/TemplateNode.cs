using System.Collections.Generic;

namespace ExcelRenderer.Mapping;

/// <summary>A template row or an explicitly delimited array block.</summary>
internal sealed class TemplateNode
{
    /// <summary>Initializes a new instance of the <see cref="TemplateNode"/> class.</summary>
    /// <param name="firstRow">The first row.</param>
    /// <param name="lastRow">The last row.</param>
    /// <param name="location">The location.</param>
    /// <param name="path">The path.</param>
    /// <param name="alias">The alias.</param>
    /// <param name="children">The children.</param>
    internal TemplateNode(int firstRow, int lastRow, TemplateCell location, DataPath? path = null, string? alias = null, List<TemplateNode>? children = null)
    {
        FirstRow = firstRow;
        LastRow = lastRow;
        Location = location;
        Path = path;
        Alias = alias;
        Children = children;
    }

    /// <summary>Gets the first original row.</summary>
    internal int FirstRow { get; }

    /// <summary>Gets the last used template row.</summary>
    internal int LastRow { get; }

    /// <summary>Gets or sets the original diagnostic cell.</summary>
    internal TemplateCell Location { get; set; }

    /// <summary>Gets or sets the compiled data path.</summary>
    internal DataPath? Path { get; set; }

    /// <summary>Gets the array element alias.</summary>
    internal string? Alias { get; }

    /// <summary>Gets the nested template regions.</summary>
    internal List<TemplateNode>? Children { get; }

    /// <summary>Gets the mapped cells in a row.</summary>
    internal List<TemplateCell> Cells { get; } = new List<TemplateCell>();

    /// <summary>Gets a value indicating whether this node is an explicit block.</summary>
    internal bool IsBlock => Children is not null;
}
