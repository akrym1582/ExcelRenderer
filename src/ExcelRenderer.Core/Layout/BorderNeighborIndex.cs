using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Finds saved border owners just outside a placement without expanding empty cells.</summary>
internal sealed class BorderNeighborIndex
{
    private readonly Dictionary<(bool Horizontal, int Boundary), BandIndex<SourceEdge>> edges;

    /// <summary>Initializes a new instance of the <see cref="BorderNeighborIndex"/> class.</summary>
    /// <param name="sheet">The final styled sheet, before drawing selection.</param>
    internal BorderNeighborIndex(ReportSheet sheet)
    {
        var groups = new Dictionary<(bool, int), List<(SourceEdge, double, double)>>();
        foreach (var (address, cell) in sheet.BorderSourceCells ?? sheet.Cells)
        {
            Add(address, address, cell.RowSpan, cell.ColumnSpan, cell.Style.Border);
            foreach (var fragment in cell.MergedBorders ?? [])
            {
                Add(address, fragment.Address, 1, 1, fragment.Border);
            }
        }

        edges = groups.ToDictionary(pair => pair.Key, pair => new BandIndex<SourceEdge>(pair.Value));

        void Add(CellAddress owner, CellAddress address, int rowSpan, int columnSpan, BorderStyle? border)
        {
            if (border is null || sheet.Columns.GetValueOrDefault(address.Column, new()).IsHidden ||
                sheet.Rows.GetValueOrDefault(address.Row, new(sheet.DefaultRowHeight, sheet.DefaultRowsHidden)).IsHidden)
            {
                return;
            }

            Side(true, address.Row, address.Column, address.Column + columnSpan, border.Top, false);
            Side(true, address.Row + rowSpan, address.Column, address.Column + columnSpan, border.Bottom, true);
            Side(false, address.Column, address.Row, address.Row + rowSpan, border.Left, false);
            Side(false, address.Column + columnSpan, address.Row, address.Row + rowSpan, border.Right, true);

            void Side(bool horizontal, int boundary, int start, int end, BorderSide? side, bool preferred)
            {
                if (side is null)
                {
                    return;
                }

                var key = (horizontal, boundary);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = [];
                    groups.Add(key, group);
                }

                group.Add((new(owner, start, end, side, preferred), start, end));
            }
        }
    }

    /// <summary>Gets a value indicating whether the saved sheet has any visible border owners.</summary>
    internal bool HasEdges => edges.Count > 0;

    /// <summary>Tests sparse saved edges on the perimeter of an otherwise empty print area.</summary>
    /// <param name="area">The inclusive printed range.</param>
    /// <returns>Whether an external owner can make the print area visible.</returns>
    internal bool HasBoundary(CellRange area) =>
        Has(true, area.First.Row, area.First.Column, area.Last.Column + 1) ||
        Has(true, area.Last.Row + 1, area.First.Column, area.Last.Column + 1) ||
        Has(false, area.First.Column, area.First.Row, area.Last.Row + 1) ||
        Has(false, area.Last.Column + 1, area.First.Row, area.Last.Row + 1);

    /// <summary>Projects only neighboring border intervals, never their text or backgrounds.</summary>
    /// <param name="cells">The placed content cells.</param>
    /// <param name="context">The visible source axes.</param>
    /// <param name="scale">The source-to-page scale.</param>
    /// <param name="isPlaced">Tests whether an owner is already included.</param>
    /// <returns>Border-only cells for the same resolver as normal and merged edges.</returns>
    internal IEnumerable<RenderCell> Collect(IEnumerable<RenderCell> cells, ReportLayoutContext context, double scale, Func<CellAddress, bool> isPlaced)
    {
        if (!HasEdges)
        {
            yield break;
        }

        var collector = new PlacementCollector(this, context, scale, isPlaced);
        foreach (var cell in cells)
        {
            collector.Collect(cell);
            foreach (var border in collector.Borders)
            {
                yield return border;
            }
        }
    }

    private bool Has(bool horizontal, int boundary, int start, int end) =>
        edges.TryGetValue((horizontal, boundary), out var band) && band.Query(start, end).Any();

    private sealed class PlacementCollector
    {
        private readonly BorderNeighborIndex index;
        private readonly ReportLayoutContext context;
        private readonly double scale;
        private readonly Func<CellAddress, bool> isPlaced;
        private readonly int[] columns;
        private readonly int[] rows;
        private readonly Action<SourceEdge> visitor;
        private RenderCell cell = null!;
        private bool horizontal;
        private int start;
        private int end;
        private double position;

        internal PlacementCollector(BorderNeighborIndex index, ReportLayoutContext context, double scale, Func<CellAddress, bool> isPlaced)
        {
            this.index = index;
            this.context = context;
            this.scale = scale;
            this.isPlaced = isPlaced;
            columns = context.ColumnLayouts.Keys.OrderBy(value => value).ToArray();
            rows = context.RowLayouts.Keys.OrderBy(value => value).ToArray();
            visitor = VisitEdge;
        }

        internal List<RenderCell> Borders { get; } = [];

        internal void Collect(RenderCell current)
        {
            Borders.Clear();
            cell = current;
            if (cell.SourceAddress is not { } address)
            {
                return;
            }

            var lastRow = address.Row + cell.Cell.RowSpan;
            var lastColumn = address.Column + cell.Cell.ColumnSpan;
            Query(true, address.Row, address.Column, lastColumn, cell.Bounds.Y);
            Query(true, lastRow, address.Column, lastColumn, cell.Bounds.Y + cell.Bounds.Height);
            Query(false, address.Column, address.Row, lastRow, cell.Bounds.X);
            Query(false, lastColumn, address.Row, lastRow, cell.Bounds.X + cell.Bounds.Width);
        }

        private void Query(bool horizontal, int boundary, int start, int end, double position)
        {
            if (!index.edges.TryGetValue((horizontal, boundary), out var band))
            {
                return;
            }

            this.horizontal = horizontal;
            this.start = start;
            this.end = end;
            this.position = position;
            band.Visit(start, end, visitor);
        }

        private void VisitEdge(SourceEdge edge)
        {
            if (isPlaced(edge.Owner))
            {
                return;
            }

            var low = Map(Math.Max(start, edge.Start));
            var high = Map(Math.Min(end, edge.End));
            if (high <= low)
            {
                return;
            }

            var style = scale == 1 ? edge.Style : edge.Style with { Width = edge.Style.Width * scale };
            var bounds = horizontal
                ? new ReportRect(low, position - (edge.Preferred ? 1 : 0), high - low, 1)
                : new ReportRect(position - (edge.Preferred ? 1 : 0), low, 1, high - low);
            var border = horizontal
                ? edge.Preferred ? new BorderStyle(Bottom: style) : new BorderStyle(Top: style)
                : edge.Preferred ? new BorderStyle(Right: style) : new BorderStyle(Left: style);
            Borders.Add(new(new(null, CellStyle.Default with { Border = border }), bounds)
            {
                SourceAddress = edge.Owner,
                ClipBounds = cell.ClipBounds,
            });
        }

        private double Map(int coordinate)
        {
            var address = cell.SourceAddress!.Value;
            var axis = horizontal ? columns : rows;
            var found = Array.BinarySearch(axis, coordinate);
            if (found < 0)
            {
                found = ~found;
            }

            if (coordinate >= end || found >= axis.Length || axis[found] >= end)
            {
                return horizontal ? cell.Bounds.X + cell.Bounds.Width : cell.Bounds.Y + cell.Bounds.Height;
            }

            return horizontal
                ? cell.Bounds.X + ((context.ColumnLayouts[axis[found]].X - context.ColumnLayouts[address.Column].X) * scale)
                : cell.Bounds.Y + ((context.RowLayouts[axis[found]].Y - context.RowLayouts[address.Row].Y) * scale);
        }
    }

    private sealed record SourceEdge(CellAddress Owner, int Start, int End, BorderSide Style, bool Preferred);
}
