using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed class ColumnLayoutPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        var x = 0d;
        foreach (var column in context.VisibleColumns)
        {
            var width = context.Sheet.Columns.GetValueOrDefault(column, new()).Width;
            context.ColumnLayouts[column] = new(column, x, width);
            x += width;
        }
    }
}
