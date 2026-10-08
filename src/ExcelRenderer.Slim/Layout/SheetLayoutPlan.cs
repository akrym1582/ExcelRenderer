using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>Represents sheet layout plan data within a conversion.</summary>
internal sealed class SheetLayoutPlan
{
    private readonly ReportSheet sheet;
    private readonly SheetGeometry geometry;
    private readonly CellRange? printArea;
    private readonly IReadOnlyList<int> visibleColumns;
    private readonly IReadOnlyList<int> visibleRows;
    private readonly Dictionary<int, ColumnLayout> columns;
    private readonly Dictionary<int, RowLayout> rows;
    private readonly (CellAddress Address, ReportRect Bounds)[] cells;
    private readonly BandIndex<int> cellBands;
    private readonly int[] repeatedRows;
    private readonly HashSet<int> titleRows;
    private readonly HashSet<int> titleColumns;
    private readonly SheetObjectLayoutIndex? objects;

    /// <summary>Initializes a new instance of the <see cref="SheetLayoutPlan"/> class.</summary>
    /// <param name="context">The context used by this operation.</param>
    internal SheetLayoutPlan(ReportLayoutContext context)
    {
        sheet = context.Sheet;
        geometry = context.Geometry;
        printArea = context.PrintArea;
        visibleColumns = context.VisibleColumns;
        visibleRows = context.VisibleRows;
        columns = context.ColumnLayouts;
        rows = context.RowLayouts;
        Pages = PaginationPass.Plan(context);
        objects = (sheet.Images?.Count ?? 0) > 0 ? new(context) : null;
        cells = CellBoundsPass.EnumerateBounds(context).ToArray();
        cellBands = new(cells.Select((cell, index) => (index, cell.Bounds.Y, cell.Bounds.Y + cell.Bounds.Height)));
        titleRows = new(Pages.FirstOrDefault()?.TitleRows ?? []);
        titleColumns = new(Pages.FirstOrDefault()?.TitleColumns ?? []);
        repeatedRows = Enumerable.Range(0, cells.Length).Where(index => titleRows.Contains(cells[index].Address.Row)).ToArray();
    }

    /// <summary>Gets the planned band pairs without render payloads.</summary>
    internal IReadOnlyList<PaginationPagePlan> Pages { get; private set; }

    /// <summary>Supplies the sheet-level blank page when every print-area plan is empty.</summary>
    internal void EnsureEmptyPage() => Pages = [new(null, null, [], [], [], [], 0, 0, 0, 0, 1)];

    /// <summary>Builds only the requested page using page-local text layouts.</summary>
    /// <param name="index">The index used by this operation.</param>
    /// <param name="number">The number used by this operation.</param>
    /// <param name="count">The count used by this operation.</param>
    /// <param name="measurer">The measurer used by this operation.</param>
    /// <param name="cancellationToken">The page-build cancellation token.</param>
    /// <returns>The planned or generated result.</returns>
    internal RenderPage Build(int index, int number, int count, ITextMeasurer measurer, CancellationToken cancellationToken = default)
    {
        var plan = Pages[index];
        var context = new ReportLayoutContext(sheet, measurer, geometry)
        {
            CancellationToken = cancellationToken,
            PrintArea = printArea,
            VisibleColumns = visibleColumns,
            VisibleRows = visibleRows,
            ObjectLayouts = objects,
        };
        context.ColumnLayouts = columns;
        context.RowLayouts = rows;

        if (plan.Horizontal is { } horizontal && plan.Vertical is { } vertical)
        {
            var selection = new PageCellSelection(horizontal, vertical, titleColumns, titleRows, plan.TitleColumnEnd, plan.TitleRowEnd);
            var candidates = cellBands.Query(vertical.Start, vertical.End);
            if (selection.RepeatRows)
            {
                candidates = candidates.Concat(repeatedRows);
            }

            context.CandidateAddresses = candidates.Distinct().OrderBy(i => i).Where(i =>
            {
                var (address, bounds) = cells[i];
                var cell = sheet.Cells[address];
                return selection.Contains(address, bounds, cell.ColumnSpan > 1 || cell.RowSpan > 1);
            }).Select(i => cells[i].Address).ToArray();
        }
        else
        {
            context.CandidateAddresses = [];
        }

        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);
        return PaginationPass.Materialize(context, plan, number, count);
    }
}
