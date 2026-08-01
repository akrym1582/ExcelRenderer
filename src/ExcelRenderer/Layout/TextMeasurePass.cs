using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed class TextMeasurePass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        foreach (var (address, cell) in context.Sheet.Cells)
        {
            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column)) continue;
            var availableWidth = Enumerable.Range(address.Column, cell.ColumnSpan)
                .Where(context.ColumnLayouts.ContainsKey).Sum(x => context.ColumnLayouts[x].Width);
            context.TextSizes[address] = context.TextMeasurer.Measure(
                cell.Text ?? string.Empty, cell.Style.Font, availableWidth, cell.Style.WrapText);
        }
    }
}
