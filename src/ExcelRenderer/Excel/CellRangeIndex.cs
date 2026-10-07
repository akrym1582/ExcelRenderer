using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>Queries existing cell addresses by sorted row and column indices.</summary>
internal sealed class CellRangeIndex
{
    private readonly int[] _rows;
    private readonly CellAddress[][] _addresses;

    /// <summary>Initializes a new instance of the <see cref="CellRangeIndex"/> class.</summary>
    /// <param name="addresses">The existing addresses.</param>
    internal CellRangeIndex(IEnumerable<CellAddress> addresses)
    {
        var groups = addresses.GroupBy(address => address.Row).OrderBy(group => group.Key).ToArray();
        _rows = groups.Select(group => group.Key).ToArray();
        _addresses = groups.Select(group => group.OrderBy(address => address.Column).ToArray()).ToArray();
    }

    /// <summary>Enumerates existing addresses inside a rectangle.</summary>
    /// <param name="range">The inclusive cell range.</param>
    /// <returns>The indexed addresses; callers may filter entries removed since indexing.</returns>
    internal IEnumerable<CellAddress> Query(CellRange range)
    {
        var row = Array.BinarySearch(_rows, range.First.Row);
        row = row >= 0 ? row : ~row;
        for (; row < _rows.Length && _rows[row] <= range.Last.Row; row++)
        {
            var addresses = _addresses[row];
            var low = 0;
            var high = addresses.Length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (addresses[middle].Column < range.First.Column)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            for (var column = low; column < addresses.Length && addresses[column].Column <= range.Last.Column; column++)
            {
                yield return addresses[column];
            }
        }
    }
}
