using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed class ReportLayoutEngine
{
    private readonly IReadOnlyList<IReportLayoutPass> passes;

    public ReportLayoutEngine(ITextMeasurer textMeasurer)
    {
        passes =
        [
            new NormalizePass(), new ResolvePrintAreaPass(), new HiddenRowColumnPass(),
            new ColumnLayoutPass(), new RowLayoutPass(), new TextMeasurePass(),
            new CellBoundsPass(), new PaginationPass()
        ];
        TextMeasurer = textMeasurer;
    }

    public ITextMeasurer TextMeasurer { get; }

    public RenderDocument Layout(ReportSheet sheet)
    {
        var context = new ReportLayoutContext(sheet, TextMeasurer);
        foreach (var pass in passes)
            pass.Execute(context);
        return context.RenderDocument ?? new RenderDocument([]);
    }
}
