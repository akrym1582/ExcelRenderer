using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

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
    private readonly BandIndex<int> _rowBands;
    private readonly BandIndex<int> _columnBands;

    private readonly (ReportImage Image, ReportRect Bounds, ReportRect Visual)[] _images;
    private readonly (ReportShape Shape, ReportRect Bounds, ReportRect Visual)[] _shapes;
    private readonly BandIndex<int> _imageBands;
    private readonly BandIndex<int> _shapeBands;

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
        _rowBands = new(bodyRows.Select(row => (row, context.RowLayouts[row].Y, context.RowLayouts[row].Y + Epsilon)));
        _columnBands = new(bodyColumns.Select(column => (column, context.ColumnLayouts[column].X, context.ColumnLayouts[column].X + Epsilon)));
        var images = new List<(ReportImage Image, ReportRect Bounds, ReportRect Visual)>();
        foreach (var image in context.Sheet.Images ?? [])
        {
            if (DrawingAnchorResolver.TryResolve(
                context, image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, image.DrawingAnchor, out var bounds))
            {
                images.Add((image, bounds, ObjectGeometry.GetVisualBounds(bounds, image.Rotation)));
            }
        }

        var shapes = new List<(ReportShape Shape, ReportRect Bounds, ReportRect Visual)>();
        foreach (var shape in context.Sheet.Shapes ?? [])
        {
            if (DrawingAnchorResolver.TryResolve(
                context, shape.Anchor, shape.OffsetX, shape.OffsetY, shape.Width, shape.Height, shape.DrawingAnchor, out var bounds))
            {
                shapes.Add((shape, bounds, context.Sheet.RequestedRange is null
                    ? ObjectGeometry.GetVisualBounds(bounds, shape.Rotation)
                    : ObjectGeometry.GetShapeVisualBounds(bounds, shape)));
            }
        }

        _images = images.ToArray();
        _shapes = shapes.ToArray();
        _imageBands = new(_images.Select((image, index) => (index, image.Visual.Y, image.Visual.Y + image.Visual.Height)));
        _shapeBands = new(_shapes.Select((shape, index) => (index, shape.Visual.Y, shape.Visual.Y + shape.Visual.Height)));
    }

    /// <summary>
    /// Selects and maps cells, images, and shapes for one page. Repeated title cells use the
    /// title origin, while body objects use the body origin and are clipped outside that body.
    /// </summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="horizontal">The half-open horizontal source band.</param>
    /// <param name="vertical">The half-open vertical source band.</param>
    /// <returns>An immutable page containing the selected and mapped objects.</returns>
    internal RenderPage Build(int pageNumber, PageBand horizontal, PageBand vertical)
    {
        var repeatColumns = horizontal.Start >= _titleColumnEnd - Epsilon;
        var repeatRows = vertical.Start >= _titleRowEnd - Epsilon;
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

        var regions = BuildRegions(placement, horizontal, vertical, repeatColumns, repeatRows);
        var candidates = _cells.Query(vertical.Start, vertical.End);
        if (repeatRows)
        {
            candidates = candidates.Concat(_repeatedRows);
        }

        var cells = candidates.GroupBy(layout => layout.Address).Select(group => group.First())
            .Where(layout => IsCellOnPage(layout, horizontal, vertical, repeatColumns, repeatRows))
            .OrderBy(layout => _cellOrder[layout.Address])
            .Select(layout => BuildCell(layout, placement, repeatColumns, repeatRows, regions))
            .ToArray();
        var images = BuildImages(horizontal, vertical, placement);
        var shapes = BuildShapes(horizontal, vertical, placement);
        return new(pageNumber, cells, images, Shapes: shapes)
        {
            SourceRegions = regions,
        };
    }

    private static ReportShape ScaleShape(ReportShape shape, double scale) => shape with
    {
        Style = shape.Style with { LineWidth = shape.Style.LineWidth * scale },
        Text = shape.Text is not { } text ? null : text with
        {
            Font = text.Font with { Size = text.Font.Size * scale },
            MarginLeft = text.MarginLeft * scale,
            MarginTop = text.MarginTop * scale,
            MarginRight = text.MarginRight * scale,
            MarginBottom = text.MarginBottom * scale,
        },
    };

    private static bool Intersects(ReportRect bounds, PageBand horizontal, PageBand vertical) =>
        bounds.X < horizontal.End && bounds.X + bounds.Width > horizontal.Start &&
        bounds.Y < vertical.End && bounds.Y + bounds.Height > vertical.Start;

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

    private bool IsCellOnPage(
        CellLayout layout,
        PageBand horizontal,
        PageBand vertical,
        bool repeatColumns,
        bool repeatRows) =>
        (_context.Sheet.RequestedRange is not null &&
            (_context.Sheet.Cells[layout.Address].ColumnSpan > 1 || _context.Sheet.Cells[layout.Address].RowSpan > 1) &&
            Intersects(layout.Bounds, horizontal, vertical)) ||
        (((layout.Bounds.X >= horizontal.Start && layout.Bounds.X < horizontal.End) ||
            (repeatColumns && _titleColumnSet.Contains(layout.Address.Column))) &&
        ((layout.Bounds.Y >= vertical.Start && layout.Bounds.Y < vertical.End) ||
            (repeatRows && _titleRowSet.Contains(layout.Address.Row))));

    private RenderCell BuildCell(CellLayout layout, PagePlacement placement, bool repeatColumns, bool repeatRows, IReadOnlyList<PageSourceRegion> regions)
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
        var source = RectangleGeometry.Bounds(
            _context.Geometry,
            new(layout.Address, new(layout.Address.Row + cell.RowSpan - 1, layout.Address.Column + cell.ColumnSpan - 1)));
        var clip = regions.FirstOrDefault(region => Math.Abs(region.Map(source).X - bounds.X) < Epsilon &&
            Math.Abs(region.Map(source).Y - bounds.Y) < Epsilon)?.PageBounds ?? placement.BodyClip;
        return new(ScaleCell(cell, _scale), bounds)
        {
            SourceAddress = layout.Address,
            ClipBounds = _context.Sheet.RequestedRange is null ? null : clip,
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

    private IReadOnlyList<RenderImage> BuildImages(PageBand horizontal, PageBand vertical, PagePlacement placement)
    {
        var result = new List<RenderImage>();
        foreach (var index in _imageBands.Query(vertical.Start, vertical.End).OrderBy(index => index))
        {
            var (image, sourceBounds, visualBounds) = _images[index];
            if (!Intersects(visualBounds, horizontal, vertical))
            {
                continue;
            }

            result.Add(new(placement.MapBodyObjectBounds(sourceBounds), image.ImageBytes, image.ZIndex)
            {
                Crop = image.Crop,
                Rotation = image.Rotation,
                FlipHorizontal = image.FlipHorizontal,
                FlipVertical = image.FlipVertical,
                ClipBounds = placement.BodyClip,
            });
        }

        return result;
    }

    private IReadOnlyList<RenderShape> BuildShapes(PageBand horizontal, PageBand vertical, PagePlacement placement)
    {
        var result = new List<RenderShape>();
        foreach (var index in _shapeBands.Query(vertical.Start, vertical.End).OrderBy(index => index))
        {
            var (shape, sourceBounds, visualBounds) = _shapes[index];
            if (!Intersects(visualBounds, horizontal, vertical))
            {
                continue;
            }

            result.Add(new(placement.MapBodyObjectBounds(sourceBounds), ScaleShape(shape, _scale))
            {
                ClipBounds = placement.BodyClip,
            });
        }

        return result;
    }

    private IReadOnlyList<PageSourceRegion> BuildRegions(
        PagePlacement placement,
        PageBand horizontal,
        PageBand vertical,
        bool repeatColumns,
        bool repeatRows)
    {
        var columns = _columnBands.QueryStarts(horizontal.Start, horizontal.End).ToArray();
        var rows = _rowBands.QueryStarts(vertical.Start, vertical.End).ToArray();
        var regions = new List<PageSourceRegion>();
        Add(columns, rows, false, false);
        if (repeatColumns)
        {
            Add(_titleColumns, rows, true, false);
        }

        if (repeatRows)
        {
            Add(columns, _titleRows, false, true);
        }

        if (repeatColumns && repeatRows)
        {
            Add(_titleColumns, _titleRows, true, true);
        }

        return regions;

        void Add(IReadOnlyList<int> cs, IReadOnlyList<int> rs, bool titleColumn, bool titleRow)
        {
            if (cs.Count == 0 || rs.Count == 0)
            {
                return;
            }

            var source = RectangleGeometry.Bounds(_context.Geometry, new(new(rs[0], cs[0]), new(rs[rs.Count - 1], cs[cs.Count - 1])));
            var x = placement.MapCellX(
                _context.ColumnLayouts[cs[0]].X,
                repeatColumns,
                titleColumn,
                _titleColumns.Count == 0 ? 0 : _context.ColumnLayouts[_titleColumns[0]].X);
            var y = placement.MapCellY(
                _context.RowLayouts[rs[0]].Y,
                repeatRows,
                titleRow,
                _titleRows.Count == 0 ? 0 : _context.RowLayouts[_titleRows[0]].Y);
            regions.Add(new(source, new(x, y, source.Width * _scale, source.Height * _scale), _scale, titleColumn || titleRow) { Cells = new(new(rs[0], cs[0]), new(rs[rs.Count - 1], cs[cs.Count - 1])) });
        }
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
