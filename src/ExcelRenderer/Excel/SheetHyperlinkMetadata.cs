using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>Deferred hyperlink metadata for one worksheet.</summary>
/// <param name="Links">Original definitions and deferred issues.</param>
/// <param name="Names">Scoped names.</param>
internal sealed record SheetHyperlinkMetadata(
    IReadOnlyList<ReportHyperlink> Links,
    IReadOnlyDictionary<string, string> Names);
