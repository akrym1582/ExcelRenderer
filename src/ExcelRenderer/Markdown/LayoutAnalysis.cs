using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

public enum LayoutSplitDirection { None, Horizontal, Vertical }

public sealed class LayoutNode
{
    public LayoutRect BoundingBox { get; init; }
    public LayoutSplitDirection SplitDirection { get; init; }
    public IReadOnlyList<LayoutNode> Children { get; init; } = Array.Empty<LayoutNode>();
    public IReadOnlyList<VisualCell> Cells { get; init; } = Array.Empty<VisualCell>();
}

public sealed class LayoutSegmenter
{
    public LayoutNode Segment(IReadOnlyList<VisualCell> cells) => Split(cells, 0);

    private static LayoutNode Split(IReadOnlyList<VisualCell> cells, int depth)
    {
        var bounds = Bounds(cells);
        if (cells.Count < 2 || depth >= 12) return Leaf(cells, bounds);
        var horizontal = BestGap(cells.Select(c => (c.Y, c.Y + c.Height)), 6);
        var vertical = BestGap(cells.Select(c => (c.X, c.X + c.Width)), 12);
        // Prefer a horizontal cut for headers; otherwise the wider proportional corridor wins.
        var useHorizontal = horizontal.Size > 0 &&
            (vertical.Size <= 0 || horizontal.Size / Math.Max(1, bounds.Height) >= vertical.Size / Math.Max(1, bounds.Width));
        var gap = useHorizontal ? horizontal : vertical;
        if (gap.Size <= 0) return Leaf(cells, bounds);
        var first = cells.Where(c => useHorizontal ? c.Y + c.Height <= gap.Start : c.X + c.Width <= gap.Start).ToArray();
        var second = cells.Where(c => useHorizontal ? c.Y >= gap.End : c.X >= gap.End).ToArray();
        if (first.Length == 0 || second.Length == 0 || first.Length + second.Length != cells.Count)
            return Leaf(cells, bounds);
        return new LayoutNode
        {
            BoundingBox = bounds,
            SplitDirection = useHorizontal ? LayoutSplitDirection.Horizontal : LayoutSplitDirection.Vertical,
            Children = new[] { Split(first, depth + 1), Split(second, depth + 1) },
            Cells = cells
        };
    }

    private static (double Start, double End, double Size) BestGap(IEnumerable<(double Start, double End)> source, double minimum)
    {
        var intervals = source.OrderBy(x => x.Start).ToArray();
        if (intervals.Length < 2) return default;
        var end = intervals[0].End;
        var best = (Start: 0d, End: 0d, Size: 0d);
        foreach (var interval in intervals.Skip(1))
        {
            var size = interval.Start - end;
            if (size >= minimum && size > best.Size) best = (end, interval.Start, size);
            end = Math.Max(end, interval.End);
        }
        return best;
    }

    internal static LayoutRect Bounds(IReadOnlyList<VisualCell> cells)
    {
        if (cells.Count == 0) return default;
        var left = cells.Min(c => c.X); var top = cells.Min(c => c.Y);
        var right = cells.Max(c => c.X + c.Width); var bottom = cells.Max(c => c.Y + c.Height);
        return new(left, top, right - left, bottom - top);
    }
    private static LayoutNode Leaf(IReadOnlyList<VisualCell> cells, LayoutRect bounds) =>
        new() { BoundingBox = bounds, Cells = cells };
}

public enum RegionType { Unknown, Title, Form, Table, Text, Image, FreeLayout }

public sealed class SheetRegion
{
    public CellRange BoundingRange { get; init; }
    public LayoutRect BoundingBox { get; init; }
    public IReadOnlyList<VisualCell> Cells { get; init; } = Array.Empty<VisualCell>();
    public IReadOnlyList<ReportImage> Images { get; init; } = Array.Empty<ReportImage>();
    public RegionType Type { get; init; }
}

public sealed class RegionDetector
{
    public IReadOnlyList<SheetRegion> Detect(LayoutNode root, ReportSheet sheet)
    {
        var leaves = Flatten(root).Where(x => x.Cells.Count > 0).ToArray();
        return leaves.Select(node => Create(node.Cells, sheet)).ToArray();
    }
    private static IEnumerable<LayoutNode> Flatten(LayoutNode node) => node.Children.Count == 0
        ? new[] { node }
        : node.Children.SelectMany(Flatten);
    private static SheetRegion Create(IReadOnlyList<VisualCell> cells, ReportSheet sheet)
    {
        var first = new CellAddress(cells.Min(c => c.Range.First.Row), cells.Min(c => c.Range.First.Column));
        var last = new CellAddress(cells.Max(c => c.Range.Last.Row), cells.Max(c => c.Range.Last.Column));
        var bounds = LayoutSegmenter.Bounds(cells);
        var images = (sheet.Images ?? Array.Empty<ReportImage>()).Where(i =>
        {
            var x = VisualCellBuilder.OffsetX(sheet, i.Anchor.Column) + i.OffsetX + i.Width / 2;
            var y = VisualCellBuilder.OffsetY(sheet, i.Anchor.Row) + i.OffsetY + i.Height / 2;
            return bounds.Contains(x, y);
        }).ToArray();
        return new() { BoundingRange = new(first, last), BoundingBox = bounds, Cells = cells,
            Images = images, Type = new RegionClassifier().Classify(cells) };
    }
}

public sealed class RegionClassifier
{
    public RegionType Classify(IReadOnlyList<VisualCell> cells)
    {
        var nonEmpty = cells.Where(c => !string.IsNullOrWhiteSpace(c.Text)).ToArray();
        if (nonEmpty.Length == 0) return RegionType.FreeLayout;
        if (nonEmpty.Length == 1 && (nonEmpty[0].Range.Last.Column > nonEmpty[0].Range.First.Column ||
            nonEmpty[0].Style.Font.Bold || nonEmpty[0].Style.Font.Size >= 14)) return RegionType.Title;
        var rows = nonEmpty.GroupBy(c => c.Range.First.Row).ToArray();
        if (rows.Length >= 2 && rows.All(r => r.Count() == 2)) return RegionType.Form;
        if (rows.Length >= 2 && rows.Select(r => r.Count()).Distinct().Count() == 1) return RegionType.Table;
        return nonEmpty.Length == 1 ? RegionType.Text : RegionType.FreeLayout;
    }
}
