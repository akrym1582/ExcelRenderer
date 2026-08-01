using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        context.PrintArea = context.Sheet.PrintArea ?? GetUsedRange(context.Sheet.Cells);
    }

    private static CellRange? GetUsedRange(IReadOnlyDictionary<CellAddress, ReportCell> cells)
    {
        if (cells.Count == 0) return null;
        return new CellRange(
            new(cells.Keys.Min(x => x.Row), cells.Keys.Min(x => x.Column)),
            new(cells.Max(x => x.Key.Row + x.Value.RowSpan - 1),
                cells.Max(x => x.Key.Column + x.Value.ColumnSpan - 1)));
    }
}
