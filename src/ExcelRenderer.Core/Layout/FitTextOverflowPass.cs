using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Includes visible trailing text in the width of a sheet fitted to one page.</summary>
internal sealed class FitTextOverflowPass : IReportLayoutPass
{
    /// <summary>Measures only ordinary text that can cross the resolved right boundary.</summary>
    /// <param name="context">The completed geometry, before scale and page planning.</param>
    public void Execute(ReportLayoutContext context)
    {
        var settings = context.Sheet.PageSettings;
        if (!PrintScaleResolver.UsesFitMode(settings) || settings.FitToPagesWide != 1 ||
            context.Sheet.RequestedRange is not null || context.PrintArea is not { } area)
        {
            return;
        }

        var columns = context.VisibleColumns.Where(column => column >= area.First.Column && column <= area.Last.Column).ToArray();
        if (columns.Length == 0)
        {
            return;
        }

        var last = context.ColumnLayouts[columns[columns.Length - 1]];
        var right = last.X + last.Width;
        var titleStart = settings.TitleColumns is { } titleColumns
            ? context.VisibleColumns.Where(column => column >= titleColumns.First && column <= titleColumns.Last)
                .Select(column => context.ColumnLayouts[column].X).DefaultIfEmpty(double.PositiveInfinity).Min()
            : double.PositiveInfinity;
        var candidates = new List<CellAddress>();
        var visited = 0;
        foreach (var (address, cell) in context.Policy.SelectSheetCells(context))
        {
            if ((visited++ & 255) == 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            if (!TextOverflowPass.CanOverflow(cell) || cell.Style.HorizontalAlignment == HorizontalAlignment.Right ||
                !context.ColumnLayouts.TryGetValue(address.Column, out var column) || !context.RowLayouts.ContainsKey(address.Row) ||
                address.Column < area.First.Column || address.Column > area.Last.Column ||
                (settings.TitleColumns is { } titles && address.Column >= titles.First && address.Column <= titles.Last))
            {
                continue;
            }

            var limits = context.Geometry.GetTextOverflow(context.Sheet).Find(address, cell.RowSpan, cell.ColumnSpan);
            var offset = column.X - context.Geometry.ColumnStart(address.Column);
            if (limits is { } edges && edges.Right + offset > right)
            {
                candidates.Add(address);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        // Keep finalized runs page-local. This temporary measurement uses the same
        // rich-text, content-inset and effective-style path as page materialization.
        var candidate = new CellAddress[1];
        var measured = new ReportLayoutContext(context.Sheet, context.TextMeasurer, context.Geometry)
        {
            Policy = context.Policy,
            CancellationToken = context.CancellationToken,
            ColumnLayouts = context.ColumnLayouts,
            RowLayouts = context.RowLayouts,
            CandidateAddresses = candidate,
            Purpose = PageBuildPurpose.FontCollection,
        };
        var measure = new TextMeasurePass();
        var bounds = new CellBoundsPass();
        var overflow = new TextOverflowPass();
        foreach (var address in candidates)
        {
            candidate[0] = address;
            measure.Execute(measured);
            bounds.Execute(measured);
            overflow.Execute(measured);
            if (measured.CellLayouts.TryGetValue(address, out var layout) && layout.TextOverflowBounds is { } limits)
            {
                var style = context.Sheet.Cells[address].Style;
                var textRight = TextOverflowPass.GetTextLeft(layout, style) + layout.TextSize.Width + (style.Font.Size / 4);
                if (layout.Bounds.X < titleStart)
                {
                    textRight = Math.Min(textRight, titleStart);
                }

                right = Math.Max(right, Math.Min(limits.Right, textRight));
            }

            measured.TextLayouts.Clear();
            measured.TextSizes.Clear();
            measured.CellLayouts.Clear();
        }

        if (right > last.X + last.Width)
        {
            context.TextOverflowPrintEnd = right;
        }
    }
}
