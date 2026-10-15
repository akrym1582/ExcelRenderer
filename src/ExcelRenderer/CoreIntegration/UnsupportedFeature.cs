using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Describes a deferred feature candidate without retaining source cell content.</summary>
/// <param name="Code">The diagnostic code.</param>
/// <param name="Message">The content-free description.</param>
/// <param name="Id">The stable worksheet-local rule identifier.</param>
/// <param name="Ranges">The applicable original ranges.</param>
internal sealed record UnsupportedFeature(string Code, string Message, string Id, IReadOnlyList<CellRange> Ranges)
{
    /// <summary>Gets a value indicating whether the feature has no cell coordinate.</summary>
    internal bool IsSheetLevel { get; init; }

    /// <summary>Gets the original drawing anchor, when the candidate is a chart.</summary>
    internal DrawingAnchor? Anchor { get; init; }
}
