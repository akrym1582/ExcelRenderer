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

        var cells = _context.CellLayouts.Values
            .Where(layout => IsCellOnPage(layout, horizontal, vertical, repeatColumns, repeatRows))
            .Select(layout => BuildCell(layout, placement, repeatColumns, repeatRows))
            .ToArray();
        var images = BuildImages(horizontal, vertical, placement);
        var shapes = BuildShapes(horizontal, vertical, placement);
        return new(pageNumber, cells, images, Shapes: shapes);
    }

    private static BorderStyle ScaleBorder(BorderStyle border, double scale)
    {
        BorderSide? Side(BorderSide? side) => side is null ? null : side with { Width = side.Width * scale };
        return new(Side(border.Left), Side(border.Top), Side(border.Right), Side(border.Bottom));
    }

    private static ReportCell ScaleCell(ReportCell cell, double scale) => cell with
    {
        Style = cell.Style with
        {
            Font = cell.Style.Font with { Size = cell.Style.Font.Size * scale },
            Border = cell.Style.Border is { } border ? ScaleBorder(border, scale) : null,
        },
    };

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

    private bool IsCellOnPage(
        CellLayout layout,
        PageBand horizontal,
        PageBand vertical,
        bool repeatColumns,
        bool repeatRows) =>
        ((layout.Bounds.X >= horizontal.Start && layout.Bounds.X < horizontal.End) ||
            (repeatColumns && _titleColumns.Contains(layout.Address.Column))) &&
        ((layout.Bounds.Y >= vertical.Start && layout.Bounds.Y < vertical.End) ||
            (repeatRows && _titleRows.Contains(layout.Address.Row)));

    private RenderCell BuildCell(CellLayout layout, PagePlacement placement, bool repeatColumns, bool repeatRows)
    {
        var isTitleColumn = _titleColumns.Contains(layout.Address.Column);
        var isTitleRow = _titleRows.Contains(layout.Address.Row);
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
        return new(ScaleCell(_context.Sheet.Cells[layout.Address], _scale), bounds)
        {
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
        foreach (var image in _context.Sheet.Images ?? [])
        {
            if (!DrawingAnchorResolver.TryResolve(
                _context,
                image.Anchor,
                image.OffsetX,
                image.OffsetY,
                image.Width,
                image.Height,
                image.DrawingAnchor,
                out var sourceBounds) ||
                !Intersects(ObjectGeometry.GetVisualBounds(sourceBounds, image.Rotation), horizontal, vertical))
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
        foreach (var shape in _context.Sheet.Shapes ?? [])
        {
            if (!DrawingAnchorResolver.TryResolve(
                _context,
                shape.Anchor,
                shape.OffsetX,
                shape.OffsetY,
                shape.Width,
                shape.Height,
                shape.DrawingAnchor,
                out var sourceBounds) ||
                !Intersects(ObjectGeometry.GetVisualBounds(sourceBounds, shape.Rotation), horizontal, vertical))
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
