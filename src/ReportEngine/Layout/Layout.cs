using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public readonly record struct ReportRect(double X, double Y, double Width, double Height);
public sealed record ColumnLayout(int Column, double X, double Width);
public sealed record RowLayout(int Row, double Y, double Height);
public sealed record CellLayout(CellAddress Address, ReportRect Bounds, TextSize TextSize);
public sealed record RenderCell(ReportCell Cell, ReportRect Bounds);
public sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes);
public sealed record RenderText(ReportRect Bounds, string Text, CellStyle Style);
public sealed record RenderPage(
    int Number,
    IReadOnlyList<RenderCell> Cells,
    IReadOnlyList<RenderImage>? Images = null,
    IReadOnlyList<RenderText>? HeaderFooterTexts = null);
public sealed record RenderDocument(IReadOnlyList<RenderPage> Pages);

public sealed class ReportLayoutContext
{
    public ReportLayoutContext(ReportSheet sheet, ITextMeasurer textMeasurer)
    {
        Sheet = sheet;
        TextMeasurer = textMeasurer;
    }

    public ReportSheet Sheet { get; }
    public ITextMeasurer TextMeasurer { get; }
    public CellRange? PrintArea { get; set; }
    public IReadOnlyList<int> VisibleColumns { get; set; } = [];
    public IReadOnlyList<int> VisibleRows { get; set; } = [];
    public Dictionary<int, ColumnLayout> ColumnLayouts { get; } = [];
    public Dictionary<int, RowLayout> RowLayouts { get; } = [];
    public Dictionary<CellAddress, TextSize> TextSizes { get; } = [];
    public Dictionary<CellAddress, CellLayout> CellLayouts { get; } = [];
    public RenderDocument? RenderDocument { get; set; }
}

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
