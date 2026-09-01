using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

public sealed record VisualCell(CellRange Range, string? Text, double X, double Y,
    double Width, double Height, CellStyle Style, string? Formula = null)
{
    public LayoutRect BoundingBox => new(X, Y, Width, Height);
}

public sealed class VisualCellBuilder
{
    public IReadOnlyList<VisualCell> Build(ReportSheet sheet)
    {
        var result = new List<VisualCell>();
        var metrics = new LayoutMetrics(sheet);
        foreach (var entry in sheet.Cells.OrderBy(x => x.Key.Row).ThenBy(x => x.Key.Column))
        {
            var address = entry.Key;
            var cell = entry.Value;
            if (IsHidden(sheet, address, cell)) continue;
            var range = new CellRange(address,
                new(address.Row + cell.RowSpan - 1, address.Column + cell.ColumnSpan - 1));
            result.Add(new(range, cell.Text, metrics.OffsetX(address.Column), metrics.OffsetY(address.Row),
                metrics.Width(range), metrics.Height(range), cell.Style, cell.Formula));
        }
        return result;
    }

    internal static double OffsetX(ReportSheet sheet, int column) =>
        Enumerable.Range(1, Math.Max(0, column - 1)).Sum(c => ColumnWidth(sheet, c));
    internal static double OffsetY(ReportSheet sheet, int row) =>
        Enumerable.Range(1, Math.Max(0, row - 1)).Sum(r => RowHeight(sheet, r));
    private static double Width(ReportSheet s, CellRange r) =>
        Enumerable.Range(r.First.Column, r.Last.Column - r.First.Column + 1).Sum(c => ColumnWidth(s, c));
    private static double Height(ReportSheet s, CellRange r) =>
        Enumerable.Range(r.First.Row, r.Last.Row - r.First.Row + 1).Sum(x => RowHeight(s, x));
    private static double ColumnWidth(ReportSheet s, int c) =>
        s.Columns.TryGetValue(c, out var value) && !value.IsHidden ? value.Width : 0;
    private static double RowHeight(ReportSheet s, int r) =>
        s.Rows.TryGetValue(r, out var value) && !value.IsHidden ? value.Height : 0;
    private static bool IsHidden(ReportSheet s, CellAddress a, ReportCell cell) =>
        Enumerable.Range(a.Column, cell.ColumnSpan).Any(c => s.Columns.TryGetValue(c, out var d) && d.IsHidden) ||
        Enumerable.Range(a.Row, cell.RowSpan).Any(r => s.Rows.TryGetValue(r, out var d) && d.IsHidden);

    private sealed class LayoutMetrics
    {
        private readonly double[] _columnOffsets;
        private readonly double[] _rowOffsets;

        public LayoutMetrics(ReportSheet sheet)
        {
            var maxColumn = sheet.Cells.Count == 0 ? 0 : sheet.Cells.Max(entry =>
                entry.Key.Column + entry.Value.ColumnSpan - 1);
            var maxRow = sheet.Cells.Count == 0 ? 0 : sheet.Cells.Max(entry =>
                entry.Key.Row + entry.Value.RowSpan - 1);
            _columnOffsets = PrefixSums(maxColumn, column => ColumnWidth(sheet, column));
            _rowOffsets = PrefixSums(maxRow, row => RowHeight(sheet, row));
        }

        public double OffsetX(int column) => _columnOffsets[column - 1];
        public double OffsetY(int row) => _rowOffsets[row - 1];
        public double Width(CellRange range) =>
            _columnOffsets[range.Last.Column] - _columnOffsets[range.First.Column - 1];
        public double Height(CellRange range) =>
            _rowOffsets[range.Last.Row] - _rowOffsets[range.First.Row - 1];

        private static double[] PrefixSums(int count, Func<int, double> size)
        {
            var offsets = new double[count + 1];
            for (var index = 1; index <= count; index++)
                offsets[index] = offsets[index - 1] + size(index);
            return offsets;
        }
    }
}
