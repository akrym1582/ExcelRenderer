using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed class NormalizePass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        // ExcelReader represents merged ranges on their top-left cell; this pass
        // remains the explicit normalization boundary for additional input providers.
    }
}

public sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        context.PrintArea = context.Sheet.PrintArea ?? GetUsedRange(context.Sheet.Cells.Keys);
    }

    private static CellRange? GetUsedRange(IEnumerable<CellAddress> addresses)
    {
        var cells = addresses.ToArray();
        return cells.Length == 0 ? null : new CellRange(
            new(cells.Min(x => x.Row), cells.Min(x => x.Column)),
            new(cells.Max(x => x.Row), cells.Max(x => x.Column)));
    }
}

public sealed class HiddenRowColumnPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        if (context.PrintArea is not { } area) return;
        context.VisibleColumns = Enumerable.Range(area.First.Column, area.Last.Column - area.First.Column + 1)
            .Where(column => !context.Sheet.Columns.GetValueOrDefault(column, new()).IsHidden).ToArray();
        context.VisibleRows = Enumerable.Range(area.First.Row, area.Last.Row - area.First.Row + 1)
            .Where(row => !context.Sheet.Rows.GetValueOrDefault(row, new()).IsHidden).ToArray();
    }
}

public sealed class ColumnLayoutPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        var x = 0d;
        foreach (var column in context.VisibleColumns)
        {
            var width = context.Sheet.Columns.GetValueOrDefault(column, new()).Width;
            context.ColumnLayouts[column] = new(column, x, width);
            x += width;
        }
    }
}

public sealed class RowLayoutPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        var y = 0d;
        foreach (var row in context.VisibleRows)
        {
            var height = context.Sheet.Rows.GetValueOrDefault(row, new()).Height;
            context.RowLayouts[row] = new(row, y, height);
            y += height;
        }
    }
}

public sealed class TextMeasurePass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        foreach (var (address, cell) in context.Sheet.Cells)
        {
            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column)) continue;
            var availableWidth = Enumerable.Range(address.Column, cell.ColumnSpan)
                .Where(context.ColumnLayouts.ContainsKey).Sum(x => context.ColumnLayouts[x].Width);
            context.TextSizes[address] = context.TextMeasurer.Measure(
                cell.Text ?? string.Empty, cell.Style.Font, availableWidth, cell.Style.WrapText);
        }
    }
}

public sealed class CellBoundsPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        foreach (var (address, cell) in context.Sheet.Cells)
        {
            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column) ||
                !context.RowLayouts.TryGetValue(address.Row, out var row)) continue;
            var width = Enumerable.Range(address.Column, cell.ColumnSpan)
                .Where(context.ColumnLayouts.ContainsKey).Sum(x => context.ColumnLayouts[x].Width);
            var height = Enumerable.Range(address.Row, cell.RowSpan)
                .Where(context.RowLayouts.ContainsKey).Sum(x => context.RowLayouts[x].Height);
            context.CellLayouts[address] = new(address, new(column.X, row.Y, width, height),
                context.TextSizes.GetValueOrDefault(address));
        }
    }
}

public sealed class PaginationPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        if (context.PrintArea is not { } || context.CellLayouts.Count == 0)
        {
            var headerFooterTexts = CreateHeaderFooterTexts(context.Sheet, 1, 1);
            context.RenderDocument = new(headerFooterTexts.Count == 0
                ? []
                : [new RenderPage(1, [], HeaderFooterTexts: headerFooterTexts)]);
            return;
        }

        var settings = context.Sheet.PageSettings;
        var usableHeight = settings.Height - settings.MarginTop - settings.MarginBottom;
        var pageStarts = new List<double> { 0 };
        var currentStart = 0d;
        foreach (var row in context.VisibleRows)
        {
            var layout = context.RowLayouts[row];
            if (layout.Y > currentStart && layout.Y + layout.Height - currentStart > usableHeight)
            {
                currentStart = layout.Y;
                pageStarts.Add(currentStart);
            }
        }

        var pages = pageStarts.Select((start, index) =>
        {
            var end = index + 1 < pageStarts.Count ? pageStarts[index + 1] : double.PositiveInfinity;
            var cells = context.CellLayouts.Values
                .Where(layout => layout.Bounds.Y >= start && layout.Bounds.Y < end)
                .Select(layout => new RenderCell(context.Sheet.Cells[layout.Address],
                    layout.Bounds with { X = layout.Bounds.X + settings.MarginLeft, Y = layout.Bounds.Y - start + settings.MarginTop }))
                .ToArray();
            var images = (context.Sheet.Images ?? [])
                .Where(image => context.RowLayouts.TryGetValue(image.Anchor.Row, out var row) &&
                    row.Y >= start && row.Y < end &&
                    context.ColumnLayouts.ContainsKey(image.Anchor.Column))
                .Select(image =>
                {
                    var column = context.ColumnLayouts[image.Anchor.Column];
                    var row = context.RowLayouts[image.Anchor.Row];
                    return new RenderImage(new(
                        column.X + image.OffsetX + settings.MarginLeft,
                        row.Y - start + image.OffsetY + settings.MarginTop,
                        image.Width,
                        image.Height),
                        image.ImageBytes);
                })
                .ToArray();
            return new RenderPage(index + 1, cells, images);
        }).ToArray();
        context.RenderDocument = new(pages.Select(page => page with
        {
            HeaderFooterTexts = CreateHeaderFooterTexts(context.Sheet, page.Number, pages.Length)
        }).ToArray());
    }

    private static IReadOnlyList<RenderText> CreateHeaderFooterTexts(ReportSheet sheet, int pageNumber, int pageCount)
    {
        if (sheet.HeaderFooter is not { } headerFooter) return [];

        var settings = sheet.PageSettings;
        var width = settings.Width - settings.MarginLeft - settings.MarginRight;
        return CreateSection(headerFooter.Header, 0, settings.MarginTop)
            .Concat(CreateSection(headerFooter.Footer, settings.Height - settings.MarginBottom, settings.MarginBottom))
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
            .Replace("&A", sheet.Name, StringComparison.OrdinalIgnoreCase);
    }
}
