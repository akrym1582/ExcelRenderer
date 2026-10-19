using System.Collections.Generic;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Styles;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;

namespace ExcelRenderer.OpenXml.Reader.Model.ConditionalFormatting;

/// <summary>Immutable saved rule and ordered application ranges; no evaluation or rendering policy is applied.</summary>
public sealed class ConditionalFormattingRuleInfo
{
    /// <summary>Initializes a new instance of the <see cref="ConditionalFormattingRuleInfo"/> class.</summary>
    /// <param name="ranges">Ordered application ranges.</param>
    /// <param name="source">Original rule attributes and children.</param>
    /// <param name="style">Resolved differential style, or null when absent or invalid.</param>
    /// <param name="isExtension">Whether the rule requires extension interpretation.</param>
    internal ConditionalFormattingRuleInfo(IReadOnlyList<CellRange> ranges, ElementInfo source, DifferentialStyleInfo? style, bool isExtension)
    {
        Ranges = ranges;
        Source = source;
        Style = style;
        IsExtension = isExtension;
    }

    /// <summary>Gets ordered ranges. The first range supplies the formula anchor.</summary>
    public IReadOnlyList<CellRange> Ranges { get; }

    /// <summary>Gets original attributes, formulas and extension identifiers.</summary>
    public ElementInfo Source { get; }

    /// <summary>Gets the resolved dxf; original dxfId remains available in Source.</summary>
    public DifferentialStyleInfo? Style { get; }

    /// <summary>Gets a value indicating whether extension interpretation is required.</summary>
    public bool IsExtension { get; }
}
