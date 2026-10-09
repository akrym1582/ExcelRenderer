using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>Queries source cell addresses through the shared Core range index.</summary>
internal sealed class CellRangeIndex
{
    private readonly ExcelRenderer.Core.Excel.CoreCellRangeIndex<CellAddress> inner;

    /// <summary>Initializes a new instance of the <see cref="CellRangeIndex"/> class.</summary>
    /// <param name="addresses">The existing addresses.</param>
    internal CellRangeIndex(IEnumerable<CellAddress> addresses) => inner = new(addresses, address => address.Row, address => address.Column);

    /// <summary>Enumerates existing addresses inside an inclusive rectangle.</summary>
    /// <param name="range">The source cell range.</param>
    /// <returns>The indexed original addresses.</returns>
    internal IEnumerable<CellAddress> Query(CellRange range) => inner.Query(range.First.Row, range.Last.Row, range.First.Column, range.Last.Column);
}
