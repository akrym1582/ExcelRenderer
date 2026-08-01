using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed class RowLayoutPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        var y = 0d;
        foreach (var row in context.VisibleRows)
        {
            var height = context.Sheet.Rows.GetValueOrDefault(row, new()).Height;
            context.RowLayouts[row] = new(row, y, height);
            y += height;
        }
    }
}
