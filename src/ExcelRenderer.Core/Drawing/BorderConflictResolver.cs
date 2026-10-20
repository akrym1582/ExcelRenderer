using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Drawing;

/// <summary>Resolves shared cell edges before any renderer expands their stroke geometry.</summary>
internal static class BorderConflictResolver
{
    /// <summary>Collects only edge metadata and sweeps sorted intervals, without retaining cells or output commands.</summary>
    /// <param name="pageNumber">The placement's page number.</param>
    /// <param name="cells">Positioned cells in any enumeration order.</param>
    /// <returns>One styled line per winning interval, with source phase preserved by clipping.</returns>
    internal static IEnumerable<DrawCommand> Generate(int pageNumber, IEnumerable<RenderCell> cells)
    {
        var groups = new Dictionary<(bool Horizontal, ReportRect? Clip), List<Edge>>();
        var count = 0;
        foreach (var cell in cells)
        {
            Add(cell.Bounds, cell.Cell.Style.Border, cell);
            foreach (var fragment in cell.MergedBorders ?? [])
            {
                Add(fragment.Bounds, fragment.Border, cell);
            }
        }

        Rendering.ConversionMetrics.Report("borderCandidates", count);
        foreach (var group in groups.OrderBy(pair => pair.Key.Horizontal).ThenBy(pair => pair.Key.Clip?.X)
            .ThenBy(pair => pair.Key.Clip?.Y).ThenBy(pair => pair.Key.Clip?.Width).ThenBy(pair => pair.Key.Clip?.Height))
        {
            var edges = group.Value;
            edges.Sort((a, b) => a.Position.CompareTo(b.Position));
            for (var first = 0; first < edges.Count;)
            {
                var end = first + 1;
                while (end < edges.Count && SamePosition(edges[first].Position, edges[end].Position))
                {
                    end++;
                }

                var position = edges[first].Position;
                foreach (var segment in Resolve(edges, first, end))
                {
                    var edge = segment.Edge;
                    var start = segment.Start;
                    var finish = segment.End;
                    var clip = group.Key.Clip;
                    if (edge.Style.LineStyle is not (BorderLineStyle.Solid or BorderLineStyle.Double or BorderLineStyle.Hair) &&
                        (!SamePosition(start, edge.Start) || !SamePosition(finish, edge.End)))
                    {
                        // Draw the original path through an interval clip. All backends then
                        // keep the source dash/slanted-dot phase without a new public command.
                        var pad = Math.Max(1, edge.Style.Width * 2);
                        var interval = group.Key.Horizontal
                            ? new ReportRect(start, edge.Position - pad, finish - start, pad * 2)
                            : new ReportRect(edge.Position - pad, start, pad * 2, finish - start);
                        clip = clip is { } existing ? RectangleGeometry.Intersect(existing, interval) : interval;
                        if (clip is null)
                        {
                            continue;
                        }

                        start = edge.Start;
                        finish = edge.End;
                    }

                    Rendering.ConversionMetrics.Report("resolvedBorderSegments", 1);
                    yield return group.Key.Horizontal
                        ? new DrawLineCommand(pageNumber, start, position, finish, position, edge.Style) { ClipBounds = clip }
                        : new DrawLineCommand(pageNumber, position, start, position, finish, edge.Style) { ClipBounds = clip };
                }

                first = end;
            }
        }

        void Add(ReportRect bounds, BorderStyle? border, RenderCell cell)
        {
            if (border is null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            Side(true, bounds.Y, bounds.X, bounds.X + bounds.Width, border.Top, false);
            Side(true, bounds.Y + bounds.Height, bounds.X, bounds.X + bounds.Width, border.Bottom, true);
            Side(false, bounds.X, bounds.Y, bounds.Y + bounds.Height, border.Left, false);
            Side(false, bounds.X + bounds.Width, bounds.Y, bounds.Y + bounds.Height, border.Right, true);

            void Side(bool horizontal, double position, double start, double end, BorderSide? side, bool preferred)
            {
                if (side is null)
                {
                    return;
                }

                var key = (horizontal, cell.ClipBounds);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = [];
                    groups.Add(key, list);
                }

                list.Add(new(position, start, end, side, preferred, cell.SourceAddress, count++));
            }
        }
    }

    /// <summary>Compares logical line priority independently of paint order and color.</summary>
    /// <param name="first">The first side.</param>
    /// <param name="second">The second side.</param>
    /// <returns>A positive value when the first side has higher fallback priority.</returns>
    internal static int CompareStyles(BorderSide first, BorderSide second)
    {
        var doubleOrder = (first.LineStyle == BorderLineStyle.Double).CompareTo(second.LineStyle == BorderLineStyle.Double);
        if (doubleOrder != 0)
        {
            return doubleOrder;
        }

        var width = first.Width.CompareTo(second.Width);
        return width != 0 ? width : Rank(first.LineStyle).CompareTo(Rank(second.LineStyle));
    }

    private static int Rank(BorderLineStyle style) => style switch
    {
        BorderLineStyle.Double => 8,
        BorderLineStyle.Solid => 7,
        BorderLineStyle.DashDotDot => 6,
        BorderLineStyle.DashDot => 5,
        BorderLineStyle.SlantDashDot => 4,
        BorderLineStyle.Dashed => 3,
        BorderLineStyle.Dotted => 2,
        BorderLineStyle.Hair => 1,
        _ => 0,
    };

    private static bool SamePosition(double first, double second) =>
        Math.Abs(first - second) <= 8 * 2.2204460492503131e-16 * Math.Max(1, Math.Max(Math.Abs(first), Math.Abs(second)));

    private static IEnumerable<Segment> Resolve(List<Edge> edges, int first, int end)
    {
        edges.Sort(first, end - first, Comparer<Edge>.Create((a, b) =>
        {
            var start = a.Start.CompareTo(b.Start);
            return start != 0 ? start : a.End.CompareTo(b.End);
        }));
        var simple = true;
        var previous = edges[first];
        for (var index = first + 1; index < end; index++)
        {
            var edge = edges[index];
            if (SamePosition(edge.Start, previous.Start) && SamePosition(edge.End, previous.End))
            {
                continue;
            }

            if (edge.Start < previous.End && !SamePosition(edge.Start, previous.End))
            {
                simple = false;
                break;
            }

            previous = edge;
        }

        if (simple)
        {
            foreach (var segment in ResolveNonOverlapping(edges, first, end))
            {
                yield return segment;
            }

            yield break;
        }

        var events = new List<(double Position, bool Start, Edge Edge)>((end - first) * 2);
        for (var index = first; index < end; index++)
        {
            var edge = edges[index];
            events.Add((edge.Start, true, edge));
            events.Add((edge.End, false, edge));
        }

        events.Sort((a, b) => a.Position.CompareTo(b.Position));
        var active = new SortedSet<Edge>(Comparer<Edge>.Create(CompareEdges));
        Segment? pending = null;
        for (var index = 0; index < events.Count;)
        {
            var position = events[index].Position;
            do
            {
                var item = events[index++];
                if (item.Start)
                {
                    active.Add(item.Edge);
                }
                else
                {
                    active.Remove(item.Edge);
                }
            }
            while (index < events.Count && SamePosition(position, events[index].Position));

            var next = index < events.Count ? events[index].Position : position;
            var winner = active.Max;
            if (winner is not null && next > position)
            {
                if (pending is { } preceding && CanJoin(preceding, winner, position))
                {
                    pending = preceding with { End = next };
                    continue;
                }

                if (pending is not null)
                {
                    yield return pending;
                }

                pending = new(winner, position, next);
            }
            else if (pending is not null)
            {
                yield return pending;
                pending = null;
            }
        }

        if (pending is not null)
        {
            yield return pending;
        }
    }

    private static IEnumerable<Segment> ResolveNonOverlapping(List<Edge> edges, int first, int end)
    {
        Segment? pending = null;
        while (first < end)
        {
            var winner = edges[first++];
            var start = winner.Start;
            var finish = winner.End;
            while (first < end && SamePosition(edges[first].Start, start) && SamePosition(edges[first].End, finish))
            {
                if (CompareEdges(edges[first], winner) > 0)
                {
                    winner = edges[first];
                }

                first++;
            }

            if (pending is not null && CanJoin(pending, winner, start))
            {
                pending = pending with { End = finish };
                continue;
            }

            if (pending is not null)
            {
                yield return pending;
            }

            pending = new(winner, start, finish);
        }

        if (pending is not null)
        {
            yield return pending;
        }
    }

    private static bool CanJoin(Segment previous, Edge winner, double start) => SamePosition(previous.End, start) &&
        (previous.Edge == winner || (previous.Edge.Style == winner.Style &&
            winner.Style.LineStyle is BorderLineStyle.Solid or BorderLineStyle.Double or BorderLineStyle.Hair));

    private static int CompareEdges(Edge first, Edge second)
    {
        var order = CompareStyles(first.Style, second.Style);
        if (order == 0)
        {
            order = first.Preferred.CompareTo(second.Preferred);
        }

        // Remaining ties concern same-side overlapping fragments, not neighboring
        // owners. Use source geometry/address and RGBA, never style IDs or XML order.
        if (order == 0)
        {
            order = second.Start.CompareTo(first.Start);
        }

        if (order == 0)
        {
            order = second.End.CompareTo(first.End);
        }

        if (order == 0)
        {
            order = Nullable.Compare(second.Address?.Row, first.Address?.Row);
        }

        if (order == 0)
        {
            order = Nullable.Compare(second.Address?.Column, first.Address?.Column);
        }

        if (order == 0)
        {
            order = Color(first.Style).CompareTo(Color(second.Style));
        }

        if (order == 0)
        {
            order = first.Style.Color.HasValue.CompareTo(second.Style.Color.HasValue);
        }

        // IDs distinguish identical candidates in the active set only. They cannot
        // affect visible style, phase, coordinates, or the output segment order.
        return order != 0 ? order : first.Id.CompareTo(second.Id);
    }

    private static uint Color(BorderSide side)
    {
        var color = side.Color ?? new(0, 0, 0);
        return ((uint)color.Red << 24) | ((uint)color.Green << 16) | ((uint)color.Blue << 8) | color.Alpha;
    }

    private sealed record Edge(double Position, double Start, double End, BorderSide Style, bool Preferred, CellAddress? Address, int Id);

    private sealed record Segment(Edge Edge, double Start, double End);
}
