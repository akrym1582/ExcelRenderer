using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Indexes occupied cells and merged intervals without visiting missing columns or expanding merged rows.</summary>
internal sealed class TextOverflowIndex
{
    private readonly SheetGeometry geometry;
    private readonly Dictionary<int, int[]> occupied;
    private readonly int[] occupiedRows;
    private readonly BandIndex<CellRange> merges;
    private readonly Dictionary<int, (int First, int Last)[]> rows = [];

    /// <summary>Initializes a new instance of the <see cref="TextOverflowIndex"/> class.</summary>
    /// <param name="sheet">The immutable source sheet.</param>
    /// <param name="geometry">Sheet-origin coordinates with hidden columns collapsed.</param>
    internal TextOverflowIndex(ReportSheet sheet, SheetGeometry geometry)
    {
        this.geometry = geometry;
        occupied = sheet.Cells.Where(pair => (pair.Value.HasStoredValue == true || !string.IsNullOrEmpty(pair.Value.Text) || pair.Value.Formula is not null ||
                pair.Value.FormulaSharedIndex is not null || pair.Value.FormulaMasterAddress is not null || (pair.Value.HasStoredValue is null && pair.Value.Text is not null)) &&
                geometry.ColumnStart(pair.Key.Column + 1) > geometry.ColumnStart(pair.Key.Column))
            .GroupBy(pair => pair.Key.Row).ToDictionary(group => group.Key, group => group.Select(pair => pair.Key.Column).OrderBy(column => column).ToArray());
        occupiedRows = occupied.Keys.OrderBy(row => row).ToArray();
        var ranges = sheet.MergedRanges.Concat(sheet.Cells.Where(pair => pair.Value.RowSpan > 1 || pair.Value.ColumnSpan > 1)
            .Select(pair => new CellRange(pair.Key, new(pair.Key.Row + pair.Value.RowSpan - 1, pair.Key.Column + pair.Value.ColumnSpan - 1)))).Distinct();
        merges = new(ranges.Select(range => (range, (double)range.First.Row, (double)range.Last.Row + 1)));
    }

    /// <summary>Finds the nearest occupied edges on both sides of a source cell.</summary>
    /// <param name="address">The source cell's top-left position.</param>
    /// <param name="rowSpan">The source cell's row span.</param>
    /// <param name="columnSpan">The source cell's column span.</param>
    /// <returns>Sheet-point limits, unbounded where no blocker exists, or null when a single-cell source lies inside a merge.</returns>
    internal (double Left, double Right)? Find(CellAddress address, int rowSpan = 1, int columnSpan = 1)
    {
        if (rowSpan > 1 || columnSpan > 1)
        {
            return FindMerged(address, rowSpan, columnSpan);
        }

        if (!rows.TryGetValue(address.Row, out var intervals))
        {
            var source = (occupied.GetValueOrDefault(address.Row) ?? []).Select(column => (First: column, Last: column))
                .Concat(merges.Query(address.Row, address.Row + 1).Select(range => (First: range.First.Column, Last: range.Last.Column)))
                .Where(range => geometry.ColumnStart(range.Last + 1) > geometry.ColumnStart(range.First))
                .OrderBy(range => range.First).ThenBy(range => range.Last);
            var combined = new List<(int First, int Last)>();
            foreach (var range in source)
            {
                if (combined.Count > 0 && range.First <= combined[combined.Count - 1].Last)
                {
                    var previous = combined[combined.Count - 1];
                    combined[combined.Count - 1] = (previous.First, Math.Max(previous.Last, range.Last));
                }
                else
                {
                    combined.Add(range);
                }
            }

            intervals = combined.ToArray();
            rows.Add(address.Row, intervals);
        }

        var low = 0;
        var high = intervals.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (intervals[middle].First <= address.Column)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        // Skip the source cell's own interval. A source inside a merge cannot overflow.
        var left = low - 1;
        if (left >= 0 && intervals[left].Last >= address.Column)
        {
            if (intervals[left].First != address.Column || intervals[left].Last != address.Column)
            {
                return null;
            }

            left--;
        }

        return (left >= 0 ? geometry.ColumnStart(intervals[left].Last + 1) : double.NegativeInfinity,
            low < intervals.Length ? geometry.ColumnStart(intervals[low].First) : double.PositiveInfinity);
    }

    private (double Left, double Right) FindMerged(CellAddress address, int rowSpan, int columnSpan)
    {
        var lastRow = address.Row + rowSpan - 1;
        var lastColumn = address.Column + columnSpan - 1;
        var left = double.NegativeInfinity;
        var right = double.PositiveInfinity;
        var start = Array.BinarySearch(occupiedRows, address.Row);
        for (var index = start >= 0 ? start : ~start; index < occupiedRows.Length && occupiedRows[index] <= lastRow; index++)
        {
            var row = occupiedRows[index];
            if (geometry.RowStart(row + 1) <= geometry.RowStart(row))
            {
                continue;
            }

            var columns = occupied[row];
            var before = Array.BinarySearch(columns, address.Column);
            before = (before >= 0 ? before : ~before) - 1;
            var after = Array.BinarySearch(columns, lastColumn);
            after = after >= 0 ? after + 1 : ~after;
            if (before >= 0)
            {
                left = Math.Max(left, geometry.ColumnStart(columns[before] + 1));
            }

            if (after < columns.Length)
            {
                right = Math.Min(right, geometry.ColumnStart(columns[after]));
            }
        }

        foreach (var range in merges.Query(address.Row, lastRow + 1))
        {
            if (geometry.ColumnStart(range.Last.Column + 1) <= geometry.ColumnStart(range.First.Column) ||
                geometry.RowStart(Math.Min(lastRow, range.Last.Row) + 1) <= geometry.RowStart(Math.Max(address.Row, range.First.Row)))
            {
                continue;
            }

            if (range.Last.Column < address.Column)
            {
                left = Math.Max(left, geometry.ColumnStart(range.Last.Column + 1));
            }
            else if (range.First.Column > lastColumn)
            {
                right = Math.Min(right, geometry.ColumnStart(range.First.Column));
            }
        }

        return (left, right);
    }
}
