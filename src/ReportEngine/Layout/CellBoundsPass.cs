using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed class CellBoundsPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        foreach (var (address, cell) in context.Sheet.Cells)
        {
            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column) ||
                !context.RowLayouts.TryGetValue(address.Row, out var row)) continue;
            var width = Enumerable.Range(address.Column, cell.ColumnSpan)
                .Where(context.ColumnLayouts.ContainsKey).Sum(x => context.ColumnLayouts[x].Width);
            var height = Enumerable.Range(address.Row, cell.RowSpan)
                .Where(context.RowLayouts.ContainsKey).Sum(x => context.RowLayouts[x].Height);
            context.CellLayouts[address] = new(address, new(column.X, row.Y, width, height),
                context.TextSizes.GetValueOrDefault(address));
        }
    }
}
