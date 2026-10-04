using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Keeps the full geometry of intersecting merged cells in explicit selections.</summary>
internal sealed class ExplicitRangeGeometryPass : IReportLayoutPass
{
    /// <summary>Adds original merged-cell metrics without enlarging pagination.</summary>
    /// <param name="context">The current layout.</param>
    public void Execute(ReportLayoutContext context)
    {
        if (context.Sheet.RequestedRange is not { } selected)
        {
            return;
        }

        foreach (var range in context.Sheet.MergedRanges.Where(range =>
            range.First.Row <= selected.Last.Row && range.Last.Row >= selected.First.Row &&
            range.First.Column <= selected.Last.Column && range.Last.Column >= selected.First.Column))
        {
            for (var column = range.First.Column; column <= range.Last.Column; column++)
            {
                context.ColumnLayouts[column] = new(
                    column,
                    context.Geometry.ColumnStart(column),
                    context.Geometry.ColumnStart(column + 1) - context.Geometry.ColumnStart(column));
            }

            for (var row = range.First.Row; row <= range.Last.Row; row++)
            {
                context.RowLayouts[row] = new(
                    row,
                    context.Geometry.RowStart(row),
                    context.Geometry.RowStart(row + 1) - context.Geometry.RowStart(row));
            }
        }
    }
}
