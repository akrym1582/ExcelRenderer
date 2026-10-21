using ExcelRenderer.Excel;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Searches constant target-policy bands instead of every coordinate in hidden areas.</summary>
internal sealed class ConditionalTargetSearch
{
    private readonly long workLimit;
    private readonly Func<CellAddress, bool> contains;
    private readonly int[] rows;
    private readonly int[] columns;
    private readonly bool exactBands;

    /// <summary>Initializes a new instance of the <see cref="ConditionalTargetSearch"/> class.</summary>
    /// <param name="info">Source visibility, merges and print references used by the exact predicate.</param>
    /// <param name="options">Selection and optional geometry-only page boundaries.</param>
    /// <param name="cancellationToken">Cooperative preparation cancellation.</param>
    /// <param name="workLimit">Finite internal search budget.</param>
    /// <param name="contains">The existing exact target predicate, without duplicated selection semantics.</param>
    internal ConditionalTargetSearch(WorksheetInfo info, ExcelReadOptions options, Func<CellAddress, bool> contains, long workLimit = 16000000, CancellationToken cancellationToken = default)
    {
        this.contains = contains;
        var rs = new HashSet<int> { 1 };
        var cs = new HashSet<int> { 1 };
        foreach (var row in info.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rs.Add(row.Index);
            rs.Add(row.Index + 1);
        }

        foreach (var column in info.Columns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cs.Add(column.Min);
            cs.Add(column.Max + 1);
        }

        foreach (var merge in info.MergedRanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rs.UnionWith([merge.First.Row, merge.First.Row + 1, merge.Last.Row + 1]);
            cs.UnionWith([merge.First.Column, merge.First.Column + 1, merge.Last.Column + 1]);
        }

        foreach (var range in info.PrintReferences.SelectMany(reference => reference.Ranges))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rs.UnionWith([range.FirstRow, range.LastRow + 1]);
            cs.UnionWith([range.FirstColumn, range.LastColumn + 1]);
        }

        if (options.GetSelectedRange?.Invoke(info.Sheet.Name) is { } selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rs.UnionWith([selected.FirstRow, selected.LastRow + 1]);
            cs.UnionWith([selected.FirstColumn, selected.LastColumn + 1]);
        }

        var pageBands = options.ConditionalTargetBoundaries?.Invoke(info.Sheet.Name);
        exactBands = options.IsConditionalFormattingTarget is null || pageBands is not null;
        this.workLimit = exactBands ? workLimit : Math.Min(workLimit, 2000000);
        if (pageBands is { } bands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rs.UnionWith(bands.Rows);
            cs.UnionWith(bands.Columns);
        }

        rows = rs.OrderBy(value => value).ToArray();
        columns = cs.OrderBy(value => value).ToArray();
    }

    /// <summary>Returns a proven target or absence; budget exhaustion is explicitly indeterminate.</summary>
    /// <param name="ranges">Finite affected source rectangles.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>The search state and, when found, the exact visible representative.</returns>
    internal (TargetSearchState State, CellAddress Address) Find(IEnumerable<CellRange> ranges, CancellationToken token)
    {
        long probes = 0;
        long work = 0;
        try
        {
            foreach (var range in ranges)
            {
                token.ThrowIfCancellationRequested();
                if (++work > workLimit)
                {
                    return (TargetSearchState.Indeterminate, default);
                }

                for (var row = range.First.Row; row <= range.Last.Row; row = Next(rows, row))
                {
                    token.ThrowIfCancellationRequested();
                    for (var column = range.First.Column; column <= range.Last.Column; column = Next(columns, column))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++work > workLimit)
                        {
                            return (TargetSearchState.Indeterminate, default);
                        }

                        probes++;
                        var address = new CellAddress(row, column);
                        if (contains(address))
                        {
                            return (TargetSearchState.Found, address);
                        }
                    }
                }
            }

            return (TargetSearchState.Absent, default);
        }
        finally
        {
            ConversionMetrics.Report("conditionalFormatting.limitTargetProbes", probes);
        }
    }

    private int Next(int[] boundaries, int value)
    {
        if (!exactBands)
        {
            return value + 1;
        }

        var position = Array.BinarySearch(boundaries, value);
        position = position < 0 ? ~position : position + 1;
        return position < boundaries.Length ? boundaries[position] : int.MaxValue;
    }
}
