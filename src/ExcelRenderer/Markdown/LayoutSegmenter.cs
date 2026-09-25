using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// LayoutSegmenter が表すデータと操作を提供します.
/// </summary>
public sealed class LayoutSegmenter
{
    /// <summary>
    /// Segment を実行します.
    /// </summary>
    /// <param name="cells">cells に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public LayoutNode Segment(IReadOnlyList<VisualCell> cells) => Split(cells, 0);

    /// <summary>
    /// Bounds を実行します.
    /// </summary>
    /// <param name="cells">cells に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    internal static LayoutRect Bounds(IReadOnlyList<VisualCell> cells)
    {
        if (cells.Count == 0)
        {
            return default;
        }

        var left = cells.Min(c => c.X);
        var top = cells.Min(c => c.Y);
        var right = cells.Max(c => c.X + c.Width);
        var bottom = cells.Max(c => c.Y + c.Height);
        return new(left, top, right - left, bottom - top);
    }

    private static LayoutNode Split(IReadOnlyList<VisualCell> cells, int depth)
    {
        var bounds = Bounds(cells);
        if (cells.Count < 2 || depth >= 12)
        {
            return Leaf(cells, bounds);
        }

        var horizontal = BestGap(cells.Select(c => (c.Y, c.Y + c.Height)), 6);
        var vertical = BestGap(cells.Select(c => (c.X, c.X + c.Width)), 12);

        // Prefer a horizontal cut for headers; otherwise the wider proportional corridor wins.
        var useHorizontal = horizontal.Size > 0 &&
            (vertical.Size <= 0 || horizontal.Size / Math.Max(1, bounds.Height) >= vertical.Size / Math.Max(1, bounds.Width));
        var gap = useHorizontal ? horizontal : vertical;
        if (gap.Size <= 0)
        {
            return Leaf(cells, bounds);
        }

        var first = cells.Where(c => useHorizontal ? c.Y + c.Height <= gap.Start : c.X + c.Width <= gap.Start).ToArray();
        var second = cells.Where(c => useHorizontal ? c.Y >= gap.End : c.X >= gap.End).ToArray();
        if (first.Length == 0 || second.Length == 0 || first.Length + second.Length != cells.Count)
        {
            return Leaf(cells, bounds);
        }

        return new LayoutNode
        {
            BoundingBox = bounds,
            SplitDirection = useHorizontal ? LayoutSplitDirection.Horizontal : LayoutSplitDirection.Vertical,
            Children = new[] { Split(first, depth + 1), Split(second, depth + 1) },
            Cells = cells,
        };
    }

    private static (double Start, double End, double Size) BestGap(IEnumerable<(double Start, double End)> source, double minimum)
    {
        var intervals = source.OrderBy(x => x.Start).ToArray();
        if (intervals.Length < 2)
        {
            return default;
        }

        var end = intervals[0].End;
        var best = (Start: 0d, End: 0d, Size: 0d);
        foreach (var interval in intervals.Skip(1))
        {
            var size = interval.Start - end;
            if (size >= minimum && size > best.Size)
            {
                best = (end, interval.Start, size);
            }

            end = Math.Max(end, interval.End);
        }

        return best;
    }

    private static LayoutNode Leaf(IReadOnlyList<VisualCell> cells, LayoutRect bounds) =>
        new() { BoundingBox = bounds, Cells = cells };
}
