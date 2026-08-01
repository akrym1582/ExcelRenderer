using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

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
