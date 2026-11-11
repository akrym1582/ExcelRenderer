using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Page-local candidates selected by SheetLayoutPlan: unique, in original cell order,
/// and already satisfying the page selection, including repeated titles and merged spans.</summary>
/// <param name="Addresses">The selected source addresses, borrowed until this page is released.</param>
internal sealed record SelectedPageCells(IReadOnlyList<CellAddress> Addresses);
