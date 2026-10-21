using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// Builds one immutable render page from a pair of half-open sheet-coordinate bands.
/// Coordinates and sizes are PDF points; an infinite band end includes all remaining content.
/// </summary>
internal sealed class RenderPageBuilder
{
    private const double Epsilon = 1e-7;
    private readonly ReportLayoutContext _context;
    private readonly IReadOnlyList<int> _bodyColumns;
    private readonly IReadOnlyList<int> _bodyRows;
    private readonly IReadOnlyList<int> _titleColumns;
    private readonly IReadOnlyList<int> _titleRows;
    private readonly double _titleColumnEnd;
    private readonly double _titleRowEnd;
    private readonly double _titleWidth;
    private readonly double _titleHeight;
    private readonly double _scale;

    private readonly BandIndex<CellLayout> _cells;
    private readonly CellLayout[] _repeatedRows;
    private readonly HashSet<int> _titleRowSet;
    private readonly HashSet<int> _titleColumnSet;
    private readonly Dictionary<CellAddress, int> _cellOrder;
    private readonly Dictionary<CellStyle, CellStyle> _scaledStyles = new();
    private readonly Dictionary<BorderStyle, BorderStyle> _scaledBorders = new();

    /// <summary>Initializes a new instance of the <see cref="RenderPageBuilder"/> class without mutating the supplied context.</summary>
    /// <param name="context">The completed sheet layout to read.</param>
    /// <param name="bodyColumns">Visible body-column indices.</param>
    /// <param name="bodyRows">Visible body-row indices.</param>
    /// <param name="titleColumns">Visible repeated title-column indices.</param>
    /// <param name="titleRows">Visible repeated title-row indices.</param>
    /// <param name="titleColumnEnd">The exclusive title-column end in sheet points.</param>
    /// <param name="titleRowEnd">The exclusive title-row end in sheet points.</param>
    /// <param name="titleWidth">The repeated-title width in points.</param>
    /// <param name="titleHeight">The repeated-title height in points.</param>
    /// <param name="scale">The sheet-to-page scale.</param>
    internal RenderPageBuilder(
        ReportLayoutContext context,
        IReadOnlyList<int> bodyColumns,
        IReadOnlyList<int> bodyRows,
        IReadOnlyList<int> titleColumns,
        IReadOnlyList<int> titleRows,
        double titleColumnEnd,
        double titleRowEnd,
        double titleWidth,
        double titleHeight,
        double scale)
    {
        _context = context;
        _bodyColumns = bodyColumns;
        _bodyRows = bodyRows;
        _titleColumns = titleColumns;
        _titleRows = titleRows;
        _titleColumnEnd = titleColumnEnd;
        _titleRowEnd = titleRowEnd;
        _titleWidth = titleWidth;
        _titleHeight = titleHeight;
        _scale = scale;
        _titleRowSet = new(titleRows);
        _titleColumnSet = new(titleColumns);
        _cells = new(context.CellLayouts.Values.Select(layout =>
            (layout, layout.Bounds.Y, layout.Bounds.Y + Math.Max(layout.Bounds.Height, Epsilon))));
        _repeatedRows = context.CellLayouts.Values.Where(layout => _titleRowSet.Contains(layout.Address.Row)).ToArray();
        _cellOrder = context.CellLayouts.Keys.Select((address, order) => (address, order)).ToDictionary(pair => pair.address, pair => pair.order);
        context.ObjectLayouts ??= new SheetObjectLayoutIndex(context);
    }

    /// <summary>
    /// Selects and maps cells, images, and shapes for one page. Repeated title cells use the
    /// title origin; pictures use the body origin and the PDF page boundary clip.
    /// </summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="horizontal">The half-open horizontal source band.</param>
    /// <param name="vertical">The half-open vertical source band.</param>
    /// <returns>An immutable page containing the selected and mapped objects.</returns>
    internal RenderPage Build(int pageNumber, PageBand horizontal, PageBand vertical)
    {
        var selection = _context.Policy.CreateCellSelection(horizontal, vertical, _titleColumnSet, _titleRowSet, _titleColumnEnd, _titleRowEnd, _context.Sheet);
        var repeatColumns = selection.RepeatColumns;
        var repeatRows = selection.RepeatRows;
        var horizontalEnd = double.IsFinite(horizontal.End) ? horizontal.End : LastColumnEnd();
        var verticalEnd = double.IsFinite(vertical.End) ? vertical.End : LastRowEnd();
        var placement = new PagePlacement(
            _context.Sheet.PageSettings,
            horizontal,
            vertical,
            horizontalEnd,
            verticalEnd,
            repeatColumns ? _titleWidth : 0,
            repeatRows ? _titleHeight : 0,
            _scale);

        var candidates = _cells.Query(vertical.Start, vertical.End);
        if (repeatRows)
        {
            candidates = candidates.Concat(_repeatedRows);
        }

        var cells = candidates.GroupBy(layout => layout.Address).Select(group => group.First())
            .Where(layout => selection.Contains(layout.Address, layout.Bounds, _context.Sheet.Cells[layout.Address].ColumnSpan > 1 || _context.Sheet.Cells[layout.Address].RowSpan > 1))
            .OrderBy(layout => _cellOrder[layout.Address])
            .Select(layout => BuildCell(layout, placement, repeatColumns, repeatRows))
            .ToArray();
        var pageContext = new CorePageBuildContext(_context, placement, horizontal, vertical, _bodyColumns, _bodyRows, _titleColumns, _titleRows, repeatColumns, repeatRows);
        var images = _context.Policy.BuildPageImages(pageContext);
        Rendering.ConversionMetrics.Report("corePageBuild", 1);
        var page = _context.Policy.BuildAdditionalPageContent(pageContext, new(pageNumber, cells, images));
        _context.BorderNeighbors ??= new(_context.Sheet);
        if (!_context.BorderNeighbors.HasEdges)
        {
            return page;
        }

        var placed = new HashSet<CellAddress>(cells.Select(cell => cell.SourceAddress!.Value));
        return page with { BorderOnlyCells = _context.BorderNeighbors.Collect(page.Cells.Concat(BorderRegions()), _context, _scale, placed.Contains).ToArray() };

        IEnumerable<RenderCell> BorderRegions()
        {
            var bodyColumns = Range(_bodyColumns, index => _context.ColumnLayouts[index].X, horizontal.Start, horizontal.End);
            var bodyRows = Range(_bodyRows, index => _context.RowLayouts[index].Y, vertical.Start, vertical.End);
            foreach (var region in Region(bodyColumns, bodyRows, false, false)
                .Concat(repeatColumns ? Region(_titleColumns, bodyRows, true, false) : [])
                .Concat(repeatRows ? Region(bodyColumns, _titleRows, false, true) : [])
                .Concat(repeatColumns && repeatRows ? Region(_titleColumns, _titleRows, true, true) : []))
            {
                yield return region;
            }
        }

        IEnumerable<RenderCell> Region(IReadOnlyList<int> columns, IReadOnlyList<int> rows, bool titleColumn, bool titleRow)
        {
            if (columns.Count == 0 || rows.Count == 0)
            {
                yield break;
            }

            var firstColumn = _context.ColumnLayouts[columns[0]];
            var lastColumn = _context.ColumnLayouts[columns[columns.Count - 1]];
            var firstRow = _context.RowLayouts[rows[0]];
            var lastRow = _context.RowLayouts[rows[rows.Count - 1]];
            var bounds = new ReportRect(
                placement.MapCellX(firstColumn.X, repeatColumns, titleColumn, _titleColumns.Count == 0 ? 0 : _context.ColumnLayouts[_titleColumns[0]].X),
                placement.MapCellY(firstRow.Y, repeatRows, titleRow, _titleRows.Count == 0 ? 0 : _context.RowLayouts[_titleRows[0]].Y),
                (lastColumn.X + lastColumn.Width - firstColumn.X) * _scale,
                (lastRow.Y + lastRow.Height - firstRow.Y) * _scale);
            yield return new(new(null, CellStyle.Default, lastRow.Row - firstRow.Row + 1, lastColumn.Column - firstColumn.Column + 1), bounds)
            {
                SourceAddress = new(firstRow.Row, firstColumn.Column),
                ClipBounds = _context.Sheet.RequestedRange is null ? null : bounds,
            };
        }
    }

    private static IReadOnlyList<int> Range(IReadOnlyList<int> indices, Func<int, double> position, double start, double end)
    {
        var low = 0;
        var high = indices.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (position(indices[middle]) < start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var result = new List<int>();
        while (low < indices.Count && position(indices[low]) < end)
        {
            result.Add(indices[low++]);
        }

        return result;
    }

    private BorderStyle ScaleBorder(BorderStyle border, double scale)
    {
        if (scale == 1)
        {
            return border;
        }

        if (_scaledBorders.TryGetValue(border, out var cached))
        {
            return cached;
        }

        BorderSide? Side(BorderSide? side) => side is null ? null : side with { Width = side.Width * scale };
        var result = new BorderStyle(Side(border.Left), Side(border.Top), Side(border.Right), Side(border.Bottom));
        _scaledBorders.Add(border, result);
        return result;
    }

    private ReportCell ScaleCell(ReportCell cell, double scale)
    {
        if (scale == 1)
        {
            return cell;
        }

        if (!_scaledStyles.TryGetValue(cell.Style, out var style))
        {
            style = cell.Style with
            {
                Font = cell.Style.Font with { Size = cell.Style.Font.Size * scale },
                Border = cell.Style.Border is { } border ? ScaleBorder(border, scale) : null,
            };
            _scaledStyles.Add(cell.Style, style);
        }

        return cell with { Style = style };
    }

    private RenderCell BuildCell(CellLayout layout, PagePlacement placement, bool repeatColumns, bool repeatRows)
    {
        var isTitleColumn = _titleColumnSet.Contains(layout.Address.Column);
        var isTitleRow = _titleRowSet.Contains(layout.Address.Row);
        double PageX(double x) => placement.MapCellX(
            x,
            repeatColumns,
            isTitleColumn,
            _titleColumns.Count == 0 ? 0 : _context.ColumnLayouts[_titleColumns[0]].X);
        double PageY(double y) => placement.MapCellY(
            y,
            repeatRows,
            isTitleRow,
            _titleRows.Count == 0 ? 0 : _context.RowLayouts[_titleRows[0]].Y);
        var bounds = new ReportRect(
            PageX(layout.Bounds.X),
            PageY(layout.Bounds.Y),
            layout.Bounds.Width * _scale,
            layout.Bounds.Height * _scale);
        var cell = _context.Sheet.Cells[layout.Address];
        return new(ScaleCell(cell, _scale), bounds)
        {
            DecorationScale = _scale,
            SourceAddress = layout.Address,
            ContentBounds = new(
                bounds.X + ((layout.ContentBounds.X - layout.Bounds.X) * _scale),
                bounds.Y + ((layout.ContentBounds.Y - layout.Bounds.Y) * _scale),
                layout.ContentBounds.Width * _scale,
                layout.ContentBounds.Height * _scale),
            TextLayout = _context.TextLayouts.TryGetValue(layout.Address, out var textLayout)
                ? TextLayoutTransform.Scale(textLayout, _scale)
                : null,
            MergedBorders = layout.MergedBorders?.Select(border => new RenderBorder(
                new(
                    PageX(border.Bounds.X),
                    PageY(border.Bounds.Y),
                    border.Bounds.Width * _scale,
                    border.Bounds.Height * _scale),
                ScaleBorder(border.Border, _scale))).ToArray(),
        };
    }

    private double LastColumnEnd()
    {
        var layout = _context.ColumnLayouts[_bodyColumns[_bodyColumns.Count - 1]];
        return layout.X + layout.Width;
    }

    private double LastRowEnd()
    {
        var layout = _context.RowLayouts[_bodyRows[_bodyRows.Count - 1]];
        return layout.Y + layout.Height;
    }
}
