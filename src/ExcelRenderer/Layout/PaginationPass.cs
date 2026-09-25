using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed class PaginationPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        if (context.PrintArea is not { } ||
            (context.CellLayouts.Count == 0 && (context.Sheet.Images?.Count ?? 0) == 0 && (context.Sheet.Shapes?.Count ?? 0) == 0))
        {
            var headerFooterTexts = CreateHeaderFooterTexts(context.Sheet, 1, 1);
            context.RenderDocument = new(headerFooterTexts.Count == 0
                ? []
                : [new RenderPage(1, [], HeaderFooterTexts: headerFooterTexts)]);
            return;
        }

        var settings = context.Sheet.PageSettings;
        var area = context.PrintArea.Value;
        var bodyColumns = context.VisibleColumns.Where(column => column >= area.First.Column && column <= area.Last.Column).ToArray();
        var bodyRows = context.VisibleRows.Where(row => row >= area.First.Row && row <= area.Last.Row).ToArray();
        var titleColumns = GetIndices(context.VisibleColumns, settings.TitleColumns);
        var titleRows = GetIndices(context.VisibleRows, settings.TitleRows);
        var titleWidth = GetSize(titleColumns, column => context.ColumnLayouts[column].Width);
        var titleHeight = GetSize(titleRows, row => context.RowLayouts[row].Height);
        var titleColumnEnd = titleColumns.Count == 0 ? double.NegativeInfinity
            : context.ColumnLayouts[titleColumns[titleColumns.Count - 1]].X + context.ColumnLayouts[titleColumns[titleColumns.Count - 1]].Width;
        var titleRowEnd = titleRows.Count == 0 ? double.NegativeInfinity
            : context.RowLayouts[titleRows[titleRows.Count - 1]].Y + context.RowLayouts[titleRows[titleRows.Count - 1]].Height;
        double GetColumnEnd(int column, double end) => context.Sheet.Cells
            .Where(cell => cell.Key.Column == column)
            .Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
            .Where(context.ColumnLayouts.ContainsKey)
            .Select(last => context.ColumnLayouts[last].X + context.ColumnLayouts[last].Width)
            .Append(end).Max();
        double GetRowEnd(int row, double end) => context.Sheet.Cells
            .Where(cell => cell.Key.Row == row)
            .Select(cell => cell.Key.Row + cell.Value.RowSpan - 1)
            .Where(context.RowLayouts.ContainsKey)
            .Select(last => context.RowLayouts[last].Y + context.RowLayouts[last].Height)
            .Append(end).Max();
        var scale = GetScale(context, settings, bodyColumns, bodyRows, titleColumnEnd, titleWidth,
            titleRowEnd, titleHeight, GetColumnEnd, GetRowEnd);
        var horizontalBands = CreateBands(bodyColumns,
            column => context.ColumnLayouts[column].X,
            column => context.ColumnLayouts[column].X + context.ColumnLayouts[column].Width,
            (settings.Width - settings.MarginLeft - settings.MarginRight) / scale,
            GetColumnEnd, titleColumnEnd, titleWidth);
        var verticalBands = CreateBands(bodyRows,
            row => context.RowLayouts[row].Y,
            row => context.RowLayouts[row].Y + context.RowLayouts[row].Height,
            (settings.Height - settings.MarginTop - settings.MarginBottom) / scale,
            GetRowEnd, titleRowEnd, titleHeight);

        var pageCount = horizontalBands.Count * verticalBands.Count;
        var pages = verticalBands.SelectMany((vertical, verticalIndex) => horizontalBands.Select((horizontal, horizontalIndex) =>
        {
            var repeatColumns = horizontal.Start >= titleColumnEnd - 1e-7;
            var repeatRows = vertical.Start >= titleRowEnd - 1e-7;
            var repeatedWidth = repeatColumns ? titleWidth : 0;
            var repeatedHeight = repeatRows ? titleHeight : 0;
            var cells = context.CellLayouts.Values
                .Where(layout => (layout.Bounds.X >= horizontal.Start && layout.Bounds.X < horizontal.End ||
                        repeatColumns && titleColumns.Contains(layout.Address.Column)) &&
                    (layout.Bounds.Y >= vertical.Start && layout.Bounds.Y < vertical.End ||
                        repeatRows && titleRows.Contains(layout.Address.Row)))
                .Select(layout => new RenderCell(ScaleCell(context.Sheet.Cells[layout.Address], scale), new(
                    GetPosition(layout.Bounds.X, horizontal.Start, repeatColumns, titleColumns.Contains(layout.Address.Column),
                        titleColumns, column => context.ColumnLayouts[column].X, repeatedWidth) * scale + settings.MarginLeft,
                    GetPosition(layout.Bounds.Y, vertical.Start, repeatRows, titleRows.Contains(layout.Address.Row),
                        titleRows, row => context.RowLayouts[row].Y, repeatedHeight) * scale + settings.MarginTop,
                    layout.Bounds.Width * scale,
                    layout.Bounds.Height * scale))
                {
                    MergedBorders = layout.MergedBorders?.Select(border => new RenderBorder(new(
                        GetPosition(border.Bounds.X, horizontal.Start, repeatColumns, titleColumns.Contains(layout.Address.Column),
                            titleColumns, column => context.ColumnLayouts[column].X, repeatedWidth) * scale + settings.MarginLeft,
                        GetPosition(border.Bounds.Y, vertical.Start, repeatRows, titleRows.Contains(layout.Address.Row),
                            titleRows, row => context.RowLayouts[row].Y, repeatedHeight) * scale + settings.MarginTop,
                        border.Bounds.Width * scale, border.Bounds.Height * scale), ScaleBorder(border.Border, scale))).ToArray()
                })
                .ToArray();
            var images = (context.Sheet.Images ?? [])
                .Where(image => context.RowLayouts.TryGetValue(image.Anchor.Row, out var row) &&
                    row.Y >= vertical.Start && row.Y < vertical.End &&
                    context.ColumnLayouts.TryGetValue(image.Anchor.Column, out var column) &&
                    column.X >= horizontal.Start && column.X < horizontal.End)
                .Select(image =>
                {
                    var column = context.ColumnLayouts[image.Anchor.Column];
                    var row = context.RowLayouts[image.Anchor.Row];
                    return new RenderImage(new(
                        (column.X - horizontal.Start + repeatedWidth + image.OffsetX) * scale + settings.MarginLeft,
                        (row.Y - vertical.Start + repeatedHeight + image.OffsetY) * scale + settings.MarginTop,
                        image.Width * scale,
                        image.Height * scale),
                        image.ImageBytes, image.ZIndex);
                })
                .ToArray();
            var shapes = (context.Sheet.Shapes ?? [])
                .Where(shape => context.RowLayouts.TryGetValue(shape.Anchor.Row, out var row) && row.Y >= vertical.Start && row.Y < vertical.End &&
                    context.ColumnLayouts.TryGetValue(shape.Anchor.Column, out var column) && column.X >= horizontal.Start && column.X < horizontal.End)
                .Select(shape =>
                {
                    var column = context.ColumnLayouts[shape.Anchor.Column]; var row = context.RowLayouts[shape.Anchor.Row];
                    return new RenderShape(new((column.X - horizontal.Start + repeatedWidth + shape.OffsetX) * scale + settings.MarginLeft,
                        (row.Y - vertical.Start + repeatedHeight + shape.OffsetY) * scale + settings.MarginTop,
                        shape.Width * scale, shape.Height * scale), shape);
                }).ToArray();
            return new RenderPage(verticalIndex * horizontalBands.Count + horizontalIndex + 1, cells, images, Shapes: shapes);
        })).ToArray();
        context.RenderDocument = new(pages.Select(page => page with
        {
            HeaderFooterTexts = CreateHeaderFooterTexts(context.Sheet, page.Number, pageCount)
        }).ToArray());
    }

    private static double GetScale(ReportLayoutContext context, PageSettings settings,
        IReadOnlyList<int> columns, IReadOnlyList<int> rows,
        double titleColumnEnd, double titleWidth, double titleRowEnd, double titleHeight,
        Func<int, double, double> getColumnEnd, Func<int, double, double> getRowEnd)
    {
        if (settings.Scale is > 0) return settings.Scale.Value;

        var scales = new List<double>();
        if (settings.FitToPagesWide is > 0 && columns.Count > 0)
        {
            var first = context.ColumnLayouts[columns[0]];
            var last = context.ColumnLayouts[columns[columns.Count - 1]];
            var contentWidth = last.X + last.Width - first.X;
            if (contentWidth > 0)
                scales.Add(GetFitScale(settings.FitToPagesWide.Value,
                    settings.Width - settings.MarginLeft - settings.MarginRight, contentWidth, columns,
                    column => context.ColumnLayouts[column].X,
                    column => context.ColumnLayouts[column].X + context.ColumnLayouts[column].Width,
                    getColumnEnd, titleColumnEnd, titleWidth));
        }
        if (settings.FitToPagesTall is > 0 && rows.Count > 0)
        {
            var first = context.RowLayouts[rows[0]];
            var last = context.RowLayouts[rows[rows.Count - 1]];
            var contentHeight = last.Y + last.Height - first.Y;
            if (contentHeight > 0)
                scales.Add(GetFitScale(settings.FitToPagesTall.Value,
                    settings.Height - settings.MarginTop - settings.MarginBottom, contentHeight, rows,
                    row => context.RowLayouts[row].Y,
                    row => context.RowLayouts[row].Y + context.RowLayouts[row].Height,
                    getRowEnd, titleRowEnd, titleHeight));
        }
        return scales.Count == 0 ? 1 : scales.Min();
    }

    private static double GetFitScale(int pageCount, double pageSize, double contentSize,
        IReadOnlyList<int> indices, Func<int, double> getStart, Func<int, double> getEnd,
        Func<int, double, double> getMergedEnd, double repeatedEnd, double repeatedSize)
    {
        var initialScale = pageCount * pageSize / contentSize;
        if (pageCount >= indices.Count) return initialScale;

        var lower = 0d;
        var upper = initialScale;
        while (CreateBands(indices, getStart, getEnd, pageSize / upper, getMergedEnd,
                   repeatedEnd, repeatedSize).Count <= pageCount)
            upper *= 2;

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var candidate = (lower + upper) / 2;
            if (CreateBands(indices, getStart, getEnd, pageSize / candidate, getMergedEnd,
                    repeatedEnd, repeatedSize).Count <= pageCount)
                lower = candidate;
            else
                upper = candidate;
        }
        // Avoid carrying the pagination epsilon into rendered coordinates when the
        // exact band boundary has a simple decimal representation.
        return Math.Round(lower, 8);
    }

    private static BorderStyle ScaleBorder(BorderStyle border, double scale)
    {
        BorderSide? Side(BorderSide? side) => side is null ? null : side with { Width = side.Width * scale };
        return new(Side(border.Left), Side(border.Top), Side(border.Right), Side(border.Bottom));
    }

    private static ReportCell ScaleCell(ReportCell cell, double scale)
    {
        return cell with
        {
            Style = cell.Style with
            {
                Font = cell.Style.Font with { Size = cell.Style.Font.Size * scale },
                Border = cell.Style.Border is { } border ? ScaleBorder(border, scale) : null
            }
        };
    }

    private static List<PageBand> CreateBands(
        IReadOnlyList<int> indices,
        Func<int, double> getStart,
        Func<int, double> getEnd,
        double availableSize,
        Func<int, double, double> getMergedEnd,
        double repeatedEnd = double.NegativeInfinity,
        double repeatedSize = 0)
    {
        var bands = new List<PageBand>();
        for (var position = 0; position < indices.Count;)
        {
            var start = getStart(indices[position]);
            var end = start;
            var firstPosition = position;
            var pageAvailableSize = availableSize - (start >= repeatedEnd - 1e-7 ? repeatedSize : 0);
            while (position < indices.Count)
            {
                var candidateEnd = getMergedEnd(indices[position], Math.Max(end, getEnd(indices[position])));
                if (position > firstPosition && candidateEnd - start > pageAvailableSize + 1e-7)
                    break;
                end = candidateEnd;
                position++;
            }
            bands.Add(new(start, position < indices.Count ? getStart(indices[position]) : double.PositiveInfinity));
        }
        return bands;
    }

    private static IReadOnlyList<int> GetIndices(IReadOnlyList<int> visibleIndices, IndexRange? range) =>
        range is not { } value ? [] : visibleIndices.Where(index => index >= value.First && index <= value.Last).ToArray();

    private static double GetSize(IReadOnlyList<int> indices, Func<int, double> getSize) => indices.Sum(getSize);

    private static double GetPosition(double position, double bandStart, bool repeatsTitles, bool isTitle,
        IReadOnlyList<int> titleIndices, Func<int, double> getStart, double repeatedSize)
    {
        if (!repeatsTitles) return position - bandStart;
        if (isTitle)
            return position - getStart(titleIndices[0]);
        return position - bandStart + repeatedSize;
    }

    private static IReadOnlyList<RenderText> CreateHeaderFooterTexts(ReportSheet sheet, int pageNumber, int pageCount)
    {
        if (sheet.HeaderFooter is not { } headerFooter) return [];

        var settings = sheet.PageSettings;
        var header = pageNumber == 1 && headerFooter.FirstPageHeader is not null
            ? headerFooter.FirstPageHeader
            : pageNumber % 2 == 0 && headerFooter.EvenPageHeader is not null
                ? headerFooter.EvenPageHeader
                : headerFooter.Header;
        var footer = pageNumber == 1 && headerFooter.FirstPageFooter is not null
            ? headerFooter.FirstPageFooter
            : pageNumber % 2 == 0 && headerFooter.EvenPageFooter is not null
                ? headerFooter.EvenPageFooter
                : headerFooter.Footer;
        var width = settings.Width - settings.MarginLeft - settings.MarginRight;
        return CreateSection(header, 0, settings.MarginTop)
            .Concat(CreateSection(footer, settings.Height - settings.MarginBottom, settings.MarginBottom))
            .ToArray();

        IEnumerable<RenderText> CreateSection(HeaderFooterSection section, double y, double height)
        {
            var style = CellStyle.Default with { VerticalAlignment = VerticalAlignment.Center };
            return new[]
            {
                new RenderText(new(settings.MarginLeft, y, width, height),
                    ResolveFields(section.Left), style),
                new RenderText(new(settings.MarginLeft, y, width, height),
                    ResolveFields(section.Center), style with { HorizontalAlignment = HorizontalAlignment.Center }),
                new RenderText(new(settings.MarginLeft, y, width, height),
                    ResolveFields(section.Right), style with { HorizontalAlignment = HorizontalAlignment.Right })
            }.Where(text => !string.IsNullOrEmpty(text.Text));
        }

        string ResolveFields(string text) => text
            .Replace("&P", pageNumber.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("&N", pageCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("&A", sheet.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("&D", DateTime.Today.ToShortDateString(), StringComparison.OrdinalIgnoreCase)
            .Replace("&T", DateTime.Now.ToShortTimeString(), StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct PageBand(double Start, double End);
}
