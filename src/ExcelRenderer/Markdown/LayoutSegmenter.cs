using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// セル間にある十分な幅の空白を境として、ワークシートの表示レイアウトを階層的に分割します。
/// </summary>
public sealed class LayoutSegmenter
{
    /// <summary>
    /// セル間の水平または垂直方向の空白を再帰的に探索し、レイアウトツリーを構築します。
    /// </summary>
    /// <param name="cells">分割対象となる表示セルの一覧。</param>
    /// <returns>指定したすべてのセルを包含し、空白で分割された子ノードを持つルートノード。</returns>
    public LayoutNode Segment(IReadOnlyList<VisualCell> cells) => Split(cells, 0);

    /// <summary>
    /// 指定したすべての表示セルを包含する最小の矩形を求めます。
    /// </summary>
    /// <param name="cells">表示領域を集約するセルの一覧。</param>
    /// <returns>すべてのセルを包含する矩形。セルがない場合は各値がゼロの矩形。</returns>
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
