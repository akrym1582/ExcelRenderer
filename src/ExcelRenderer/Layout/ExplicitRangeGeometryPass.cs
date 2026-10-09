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
        var core = CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context);
        new CoreIntegration.FullExplicitRangeGeometryPass().Execute(core);
        foreach (var column in core.ColumnLayouts)
        {
            context.ColumnLayouts[column.Key] = new(column.Value.Column, column.Value.X, column.Value.Width);
        }

        foreach (var row in core.RowLayouts)
        {
            context.RowLayouts[row.Key] = new(row.Value.Row, row.Value.Y, row.Value.Height);
        }
    }
}
