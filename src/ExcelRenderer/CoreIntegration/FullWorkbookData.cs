using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Preserves full metadata without putting feature interpretation in Core.</summary>
/// <param name="Shapes">Selected-sheet shapes.</param>
/// <param name="Hyperlinks">Hyperlink display and target metadata.</param>
internal sealed record FullWorkbookData(
    IReadOnlyDictionary<string, IReadOnlyList<ReportShape>> Shapes,
    IReadOnlyDictionary<string, SheetHyperlinkMetadata> Hyperlinks) : ICoreExtensionData
{
    /// <summary>Gets deferred chart candidates by original worksheet.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<UnsupportedFeature>> UnsupportedFeatures { get; init; } = new Dictionary<string, IReadOnlyList<UnsupportedFeature>>();
}
