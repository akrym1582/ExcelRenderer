using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        context.PrintArea = context.Sheet.PrintArea ?? GetUsedRange(context.Sheet);
    }

    private static CellRange? GetUsedRange(ReportSheet sheet)
    {
        var addresses = sheet.Cells.Keys
            .Concat((sheet.Images ?? []).Select(image => image.Anchor))
            .Concat((sheet.Shapes ?? []).Select(shape => shape.Anchor))
            .ToArray();
        if (addresses.Length == 0) return null;
        var lastRows = sheet.Cells
            .Select(cell => cell.Key.Row + cell.Value.RowSpan - 1)
            .Concat((sheet.Images ?? []).Select(image => image.Anchor.Row));
        lastRows = lastRows.Concat((sheet.Shapes ?? []).Select(shape => shape.Anchor.Row));
        var lastColumns = sheet.Cells
            .Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
            .Concat((sheet.Images ?? []).Select(image => image.Anchor.Column));
        lastColumns = lastColumns.Concat((sheet.Shapes ?? []).Select(shape => shape.Anchor.Column));
        return new CellRange(
            new(addresses.Min(address => address.Row), addresses.Min(address => address.Column)),
            new(lastRows.Max(), lastColumns.Max()));
    }
}
