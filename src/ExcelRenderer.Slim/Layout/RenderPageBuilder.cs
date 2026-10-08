using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

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

    private readonly (ReportImage Image, ReportRect Bounds, ReportRect Visual)[] _images;
    private readonly BandIndex<int> _imageBands;

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
        var objects = context.ObjectLayouts ?? new SheetObjectLayoutIndex(context);
        _images = objects.Images;
        _imageBands = objects.ImageBands;
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
        var selection = new PageCellSelection(horizontal, vertical, _titleColumnSet, _titleRowSet, _titleColumnEnd, _titleRowEnd);
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
        var images = BuildImages(horizontal, vertical, placement);
        return new(pageNumber, cells, images);
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

    private IReadOnlyList<RenderImage> BuildImages(PageBand horizontal, PageBand vertical, PagePlacement placement)
    {
        var result = new List<RenderImage>();
        foreach (var index in _imageBands.Query(vertical.Start, vertical.End).OrderBy(index => index))
        {
            var (image, sourceBounds, visualBounds) = _images[index];
            if (sourceBounds.X < horizontal.Start || sourceBounds.X >= horizontal.End ||
                sourceBounds.Y < vertical.Start || sourceBounds.Y >= vertical.End)
            {
                continue;
            }

            result.Add(new(placement.MapBodyObjectBounds(sourceBounds), image.ImageBytes, image.ZIndex));
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
