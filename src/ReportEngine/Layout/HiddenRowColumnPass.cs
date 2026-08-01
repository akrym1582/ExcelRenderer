using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed class HiddenRowColumnPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        if (context.PrintArea is not { } area) return;
        context.VisibleColumns = Enumerable.Range(area.First.Column, area.Last.Column - area.First.Column + 1)
            .Where(column => !context.Sheet.Columns.GetValueOrDefault(column, new()).IsHidden).ToArray();
        context.VisibleRows = Enumerable.Range(area.First.Row, area.Last.Row - area.First.Row + 1)
            .Where(row => !context.Sheet.Rows.GetValueOrDefault(row, new()).IsHidden).ToArray();
    }
}
