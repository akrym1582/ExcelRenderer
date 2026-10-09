namespace ExcelRenderer.Core.Excel;

/// <summary>Queries source addresses by common sorted row and column arithmetic.</summary>
/// <typeparam name="TAddress">The product address type.</typeparam>
internal sealed class CoreCellRangeIndex<TAddress>
{
    private readonly int[] _rows;
    private readonly TAddress[][] _addresses;
    private readonly Func<TAddress, int> column;

    /// <summary>Initializes a new instance of the <see cref="CoreCellRangeIndex{TAddress}"/> class.</summary>
    /// <param name="addresses">The existing addresses.</param>
    /// <param name="row">The row coordinate.</param>
    /// <param name="column">The column coordinate.</param>
    internal CoreCellRangeIndex(IEnumerable<TAddress> addresses, Func<TAddress, int> row, Func<TAddress, int> column)
    {
        this.column = column;
        var groups = addresses.GroupBy(row).OrderBy(group => group.Key).ToArray();
        _rows = groups.Select(group => group.Key).ToArray();
        _addresses = groups.Select(group => group.OrderBy(column).ToArray()).ToArray();
    }

    /// <summary>Enumerates existing addresses inside a rectangle.</summary>
    /// <param name="firstRow">The first inclusive row.</param>
    /// <param name="lastRow">The last inclusive row.</param>
    /// <param name="firstColumn">The first inclusive column.</param>
    /// <param name="lastColumn">The last inclusive column.</param>
    /// <returns>The indexed addresses; callers may filter entries removed since indexing.</returns>
    internal IEnumerable<TAddress> Query(int firstRow, int lastRow, int firstColumn, int lastColumn)
    {
        var row = Array.BinarySearch(_rows, firstRow);
        row = row >= 0 ? row : ~row;
        for (; row < _rows.Length && _rows[row] <= lastRow; row++)
        {
            var addresses = _addresses[row];
            var low = 0;
            var high = addresses.Length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (column(addresses[middle]) < firstColumn)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            for (var column = low; column < addresses.Length && this.column(addresses[column]) <= lastColumn; column++)
            {
                yield return addresses[column];
            }
        }
    }
}
