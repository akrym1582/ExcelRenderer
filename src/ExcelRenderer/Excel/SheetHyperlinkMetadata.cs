using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>Deferred hyperlink metadata for one worksheet.</summary>
/// <param name="Links">Original definitions and deferred issues.</param>
/// <param name="Names">Scoped names.</param>
/// <param name="UncachedDisplays">Display values for supported uncached formulas.</param>
internal sealed record SheetHyperlinkMetadata(
    IReadOnlyList<ReportHyperlink> Links,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<CellAddress, string> UncachedDisplays)
{
    /// <summary>Gets addresses actually serialized as worksheet cells.</summary>
    internal IReadOnlyCollection<CellAddress> OriginalCells { get; init; } = [];
}
