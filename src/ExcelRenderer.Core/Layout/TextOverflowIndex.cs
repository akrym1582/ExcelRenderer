using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Indexes occupied cells and merged intervals without visiting missing columns or expanding merged rows.</summary>
internal sealed class TextOverflowIndex
{
    private readonly SheetGeometry geometry;
    private readonly Dictionary<int, int[]> occupied;
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
        var ranges = sheet.MergedRanges.Concat(sheet.Cells.Where(pair => pair.Value.RowSpan > 1 || pair.Value.ColumnSpan > 1)
            .Select(pair => new CellRange(pair.Key, new(pair.Key.Row + pair.Value.RowSpan - 1, pair.Key.Column + pair.Value.ColumnSpan - 1)))).Distinct();
        merges = new(ranges.Select(range => (range, (double)range.First.Row, (double)range.Last.Row + 1)));
    }

    /// <summary>Finds the nearest occupied edges on both sides of a source cell.</summary>
    /// <param name="address">The unmerged source position.</param>
    /// <returns>Sheet-point limits, unbounded where no blocker exists, or null for a source inside a merge.</returns>
    internal (double Left, double Right)? Find(CellAddress address)
    {
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
}
