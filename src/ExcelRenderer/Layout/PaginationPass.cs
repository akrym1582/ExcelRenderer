using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed class PaginationPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        if (context.PrintArea is not { } ||
            (context.CellLayouts.Count == 0 && (context.Sheet.Images?.Count ?? 0) == 0))
        {
            var headerFooterTexts = CreateHeaderFooterTexts(context.Sheet, 1, 1);
            context.RenderDocument = new(headerFooterTexts.Count == 0
                ? []
                : [new RenderPage(1, [], HeaderFooterTexts: headerFooterTexts)]);
            return;
        }

        var settings = context.Sheet.PageSettings;
        var horizontalBands = CreateBands(context.VisibleColumns,
            column => context.ColumnLayouts[column].X,
            column => context.ColumnLayouts[column].X + context.ColumnLayouts[column].Width,
            settings.Width - settings.MarginLeft - settings.MarginRight,
            (column, end) => context.Sheet.Cells
                .Where(cell => cell.Key.Column == column)
                .Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
                .Where(context.ColumnLayouts.ContainsKey)
                .Select(last => context.ColumnLayouts[last].X + context.ColumnLayouts[last].Width)
                .Append(end).Max());
        var verticalBands = CreateBands(context.VisibleRows,
            row => context.RowLayouts[row].Y,
            row => context.RowLayouts[row].Y + context.RowLayouts[row].Height,
            settings.Height - settings.MarginTop - settings.MarginBottom,
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
                .Select(layout => new RenderCell(context.Sheet.Cells[layout.Address],
                    layout.Bounds with
                    {
                        X = layout.Bounds.X - horizontal.Start + settings.MarginLeft,
                        Y = layout.Bounds.Y - vertical.Start + settings.MarginTop
                    }))
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
                        column.X - horizontal.Start + image.OffsetX + settings.MarginLeft,
                        row.Y - vertical.Start + image.OffsetY + settings.MarginTop,
                        image.Width,
                        image.Height),
                        image.ImageBytes);
                })
                .ToArray();
            return new RenderPage(verticalIndex * horizontalBands.Count + horizontalIndex + 1, cells, images);
        })).ToArray();
        context.RenderDocument = new(pages.Select(page => page with
        {
            HeaderFooterTexts = CreateHeaderFooterTexts(context.Sheet, page.Number, pageCount)
        }).ToArray());
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
                if (position > firstPosition && candidateEnd - start > availableSize)
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
