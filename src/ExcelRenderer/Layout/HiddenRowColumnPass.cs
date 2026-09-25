using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed class HiddenRowColumnPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        if (context.PrintArea is not { } area)
        {
            return;
        }

        var settings = context.Sheet.PageSettings;
        var columns = Enumerable.Range(area.First.Column, area.Last.Column - area.First.Column + 1);
        if (settings.TitleColumns is { } titleColumns)
        {
            columns = columns.Concat(Enumerable.Range(titleColumns.First, titleColumns.Last - titleColumns.First + 1));
        }

        context.VisibleColumns = columns.Distinct().OrderBy(column => column)
            .Where(column => !context.Sheet.Columns.GetValueOrDefault(column, new()).IsHidden).ToArray();
        var rows = Enumerable.Range(area.First.Row, area.Last.Row - area.First.Row + 1);
        if (settings.TitleRows is { } titleRows)
        {
            rows = rows.Concat(Enumerable.Range(titleRows.First, titleRows.Last - titleRows.First + 1));
        }

        context.VisibleRows = rows.Distinct().OrderBy(row => row)
            .Where(row => !context.Sheet.Rows.GetValueOrDefault(row, new()).IsHidden).ToArray();
    }
}
