using System;
using System.Collections.Generic;
using ExcelRenderer.OpenXml.Reader.Model.Cells;

namespace ExcelRenderer.Mapping.Templates;

/// <summary>Source template structure independent of the expansion language and writer.</summary>
internal sealed partial class TemplateSnapshot
{
    /// <summary>Gets or sets the original sheet name.</summary>
    internal string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the original last row.</summary>
    internal int LastRow { get; set; }

    /// <summary>Gets or sets the original last column.</summary>
    internal int LastColumn { get; set; } = 1;

    /// <summary>Gets original text cells grouped by row.</summary>
    internal Dictionary<int, TemplateCell[]> TextCells { get; } = new Dictionary<int, TemplateCell[]>();

    /// <summary>Gets nonblank content counts used to validate dedicated marker rows.</summary>
    internal Dictionary<int, int> ContentCounts { get; } = new Dictionary<int, int>();

    /// <summary>Gets source merged regions.</summary>
    internal List<CellRange> MergedRanges { get; } = new List<CellRange>();

    /// <summary>Gets or sets source-specific validation without evaluating input data.</summary>
    internal Action<TemplateNode> ValidateRepeatedRange { get; set; } = _ => { };
}
