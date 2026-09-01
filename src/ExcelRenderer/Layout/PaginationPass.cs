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
        var scale = GetScale(context, settings);
        var horizontalBands = CreateBands(context.VisibleColumns,
            column => context.ColumnLayouts[column].X,
            column => context.ColumnLayouts[column].X + context.ColumnLayouts[column].Width,
            (settings.Width - settings.MarginLeft - settings.MarginRight) / scale,
            (column, end) => context.Sheet.Cells
                .Where(cell => cell.Key.Column == column)
                .Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
                .Where(context.ColumnLayouts.ContainsKey)
                .Select(last => context.ColumnLayouts[last].X + context.ColumnLayouts[last].Width)
                .Append(end).Max());
        var verticalBands = CreateBands(context.VisibleRows,
            row => context.RowLayouts[row].Y,
            row => context.RowLayouts[row].Y + context.RowLayouts[row].Height,
            (settings.Height - settings.MarginTop - settings.MarginBottom) / scale,
            (row, end) => context.Sheet.Cells
                .Where(cell => cell.Key.Row == row)
                .Select(cell => cell.Key.Row + cell.Value.RowSpan - 1)
                .Where(context.RowLayouts.ContainsKey)
                .Select(last => context.RowLayouts[last].Y + context.RowLayouts[last].Height)
                .Append(end).Max());

        var pageCount = horizontalBands.Count * verticalBands.Count;
        var pages = verticalBands.SelectMany((vertical, verticalIndex) => horizontalBands.Select((horizontal, horizontalIndex) =>
        {
            var cells = context.CellLayouts.Values
                .Where(layout => layout.Bounds.X >= horizontal.Start && layout.Bounds.X < horizontal.End &&
                    layout.Bounds.Y >= vertical.Start && layout.Bounds.Y < vertical.End)
                .Select(layout => new RenderCell(ScaleCell(context.Sheet.Cells[layout.Address], scale), new(
                    (layout.Bounds.X - horizontal.Start) * scale + settings.MarginLeft,
                    (layout.Bounds.Y - vertical.Start) * scale + settings.MarginTop,
                    layout.Bounds.Width * scale,
                    layout.Bounds.Height * scale)))
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
                        (column.X - horizontal.Start + image.OffsetX) * scale + settings.MarginLeft,
                        (row.Y - vertical.Start + image.OffsetY) * scale + settings.MarginTop,
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
                    return new RenderShape(new((column.X - horizontal.Start + shape.OffsetX) * scale + settings.MarginLeft,
                        (row.Y - vertical.Start + shape.OffsetY) * scale + settings.MarginTop, shape.Width * scale, shape.Height * scale), shape);
                }).ToArray();
            return new RenderPage(verticalIndex * horizontalBands.Count + horizontalIndex + 1, cells, images, Shapes: shapes);
        })).ToArray();
        context.RenderDocument = new(pages.Select(page => page with
        {
            HeaderFooterTexts = CreateHeaderFooterTexts(context.Sheet, page.Number, pageCount)
        }).ToArray());
    }

    private static double GetScale(ReportLayoutContext context, PageSettings settings)
    {
        if (settings.Scale is > 0) return settings.Scale.Value;

        var scales = new List<double>();
        if (settings.FitToPagesWide is > 0 && context.VisibleColumns.Count > 0)
        {
            var first = context.ColumnLayouts[context.VisibleColumns[0]];
            var last = context.ColumnLayouts[context.VisibleColumns[context.VisibleColumns.Count - 1]];
            var contentWidth = last.X + last.Width - first.X;
            if (contentWidth > 0)
                scales.Add(settings.FitToPagesWide.Value *
                    (settings.Width - settings.MarginLeft - settings.MarginRight) / contentWidth);
        }
        if (settings.FitToPagesTall is > 0 && context.VisibleRows.Count > 0)
        {
            var first = context.RowLayouts[context.VisibleRows[0]];
            var last = context.RowLayouts[context.VisibleRows[context.VisibleRows.Count - 1]];
            var contentHeight = last.Y + last.Height - first.Y;
            if (contentHeight > 0)
                scales.Add(settings.FitToPagesTall.Value *
                    (settings.Height - settings.MarginTop - settings.MarginBottom) / contentHeight);
        }
        return scales.Count == 0 ? 1 : scales.Min();
    }

    private static ReportCell ScaleCell(ReportCell cell, double scale)
    {
        BorderSide? ScaleSide(BorderSide? side) => side is null ? null : side with { Width = side.Width * scale };
        var border = cell.Style.Border;
        var scaledBorder = border is null ? null : border with
        {
            Left = ScaleSide(border.Left),
            Top = ScaleSide(border.Top),
            Right = ScaleSide(border.Right),
            Bottom = ScaleSide(border.Bottom)
        };
        return cell with
        {
            Style = cell.Style with
            {
                Font = cell.Style.Font with { Size = cell.Style.Font.Size * scale },
                Border = scaledBorder
            }
        };
    }

    private static List<PageBand> CreateBands(
        IReadOnlyList<int> indices,
        Func<int, double> getStart,
        Func<int, double> getEnd,
        double availableSize,
        Func<int, double, double> getMergedEnd)
    {
        var bands = new List<PageBand>();
        for (var position = 0; position < indices.Count;)
        {
            var start = getStart(indices[position]);
            var end = start;
            var firstPosition = position;
            while (position < indices.Count)
            {
                var candidateEnd = getMergedEnd(indices[position], Math.Max(end, getEnd(indices[position])));
                if (position > firstPosition && candidateEnd - start > availableSize + 1e-7)
                    break;
                end = candidateEnd;
                position++;
            }
            bands.Add(new(start, position < indices.Count ? getStart(indices[position]) : double.PositiveInfinity));
        }
        return bands;
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
