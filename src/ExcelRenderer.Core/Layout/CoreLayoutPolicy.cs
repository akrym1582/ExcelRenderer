using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Selects coarse product geometry and page content while Core owns planning and placement.</summary>
internal class CoreLayoutPolicy
{
    /// <summary>Resolves an axis origin before cell geometry is built.</summary>
    /// <param name="context">The sheet geometry context.</param>
    /// <param name="index">The one-based row or column.</param>
    /// <param name="column">Whether this is the horizontal axis.</param>
    /// <param name="position">The visible-axis cumulative position.</param>
    /// <returns>The source axis position.</returns>
    internal virtual double ResolveAxisPosition(ReportLayoutContext context, int index, bool column, double position) => position;

    /// <summary>Snapshots the product geometry pipeline outside all constructors.</summary>
    /// <returns>The deliberately ordered geometry passes.</returns>
    internal virtual IEnumerable<IReportLayoutPass> GetGeometryPasses() =>
        [new NormalizePass(), new ResolvePrintAreaPass(), new HiddenRowColumnPass(), new ColumnLayoutPass(), new RowLayoutPass()];

    /// <summary>Resolves cells and supplied object bounds with shared half-open range arithmetic.</summary>
    /// <param name="context">The original geometry.</param>
    /// <returns>The automatically resolved range.</returns>
    internal virtual CellRange? ResolveUsedRange(ReportLayoutContext context) => ResolvePrintAreaPass.GetUsedRange(context.Sheet, context.Geometry, GetObjectGeometry(context));

    /// <summary>Supplies original object rectangles before range and page planning.</summary>
    /// <param name="context">The original or completed geometry.</param>
    /// <returns>The object's neutral geometry in source order.</returns>
    internal virtual IReadOnlyList<CoreObjectGeometry> GetObjectGeometry(ReportLayoutContext context)
    {
        var result = new List<CoreObjectGeometry>();
        foreach (var image in context.Sheet.Images ?? [])
        {
            var bounds = ObjectGeometry.GetSheetRect(context.Geometry, image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, image.DrawingAnchor);
            if (context.PrintArea is { } area)
            {
                var printed = RectangleGeometry.Bounds(context.Geometry, area);
                if (bounds.X < printed.X || bounds.X >= printed.X + printed.Width || bounds.Y < printed.Y || bounds.Y >= printed.Y + printed.Height)
                {
                    continue;
                }
            }

            var anchor = image.DrawingAnchor?.Kind == DrawingAnchorKind.Absolute ? new CellAddress(context.Geometry.RowAt(bounds.Y), context.Geometry.ColumnAt(bounds.X)) : image.DrawingAnchor?.From ?? image.Anchor;
            result.Add(new(anchor, bounds, bounds, image));
        }

        return result;
    }

    /// <summary>Resolves page-candidate and builder selection from the same contract.</summary>
    /// <param name="horizontal">The horizontal body band.</param>
    /// <param name="vertical">The vertical body band.</param>
    /// <param name="titleColumns">Repeated column membership.</param>
    /// <param name="titleRows">Repeated row membership.</param>
    /// <param name="titleColumnEnd">The exclusive title column end.</param>
    /// <param name="titleRowEnd">The exclusive title row end.</param>
    /// <param name="sheet">The source sheet.</param>
    /// <returns>The conversion-local selection.</returns>
    internal virtual PageCellSelection CreateCellSelection(PageBand horizontal, PageBand vertical, HashSet<int> titleColumns, HashSet<int> titleRows, double titleColumnEnd, double titleRowEnd, ReportSheet sheet) =>
        new(horizontal, vertical, titleColumns, titleRows, titleColumnEnd, titleRowEnd);

    /// <summary>Builds only images whose source origin starts in the body band.</summary>
    /// <param name="context">The single page context.</param>
    /// <returns>The placed basic images.</returns>
    internal virtual IReadOnlyList<RenderImage> BuildPageImages(CorePageBuildContext context)
    {
        var result = new List<RenderImage>();
        var objects = context.Layout.ObjectLayouts!;
        foreach (var index in objects.Bands.Query(context.Vertical.Start, context.Vertical.End).OrderBy(index => index))
        {
            var item = objects.Objects[index];
            if (item.Image is not { } image || item.Bounds.X < context.Horizontal.Start || item.Bounds.X >= context.Horizontal.End || item.Bounds.Y < context.Vertical.Start || item.Bounds.Y >= context.Vertical.End)
            {
                continue;
            }

            result.Add(new(context.Placement.MapBodyObjectBounds(item.Bounds), image.ImageBytes, image.ZIndex));
        }

        return result;
    }

    /// <summary>Completes product page content after basic cell and image placement.</summary>
    /// <param name="context">The page-local geometry and candidates.</param>
    /// <param name="page">The basic placed page.</param>
    /// <returns>The completed page.</returns>
    internal virtual RenderPage BuildAdditionalPageContent(CorePageBuildContext context, RenderPage page) => page;

    /// <summary>Determines whether body pagination is required for this sheet.</summary>
    /// <param name="context">The completed geometry.</param>
    /// <returns>Whether the product has printable body content.</returns>
    internal virtual bool HasPageContent(ReportLayoutContext context) => context.Sheet.Cells.Keys.Any(address => context.ColumnLayouts.ContainsKey(address.Column) && context.RowLayouts.ContainsKey(address.Row)) || (context.Sheet.Images?.Count ?? 0) > 0;

    /// <summary>Selects cell spans that extend the automatic page bands.</summary>
    /// <param name="context">The completed geometry.</param>
    /// <returns>The product's pagination spans.</returns>
    internal virtual IEnumerable<KeyValuePair<CellAddress, ReportCell>> GetPaginationCells(ReportLayoutContext context) => context.Sheet.Cells;

    /// <summary>Selects sheet cells before page-local measurement and geometry.</summary>
    /// <param name="context">The sheet and page candidates.</param>
    /// <returns>The selected cells in their original order.</returns>
    internal virtual IEnumerable<KeyValuePair<CellAddress, ReportCell>> SelectSheetCells(ReportLayoutContext context) => context.CandidateCells;

    /// <summary>Normalizes display text for the product's measurement contract.</summary>
    /// <param name="text">The original display text.</param>
    /// <param name="style">Original style information.</param>
    /// <returns>The measurement text.</returns>
    internal virtual string PrepareMeasurementText(string text, CellStyle style) => text;

    /// <summary>Resolves visual object bounds after coordinate mapping.</summary>
    /// <param name="item">Original object metadata.</param>
    /// <param name="bounds">Mapped object coordinates.</param>
    /// <param name="sheet">Source selection metadata.</param>
    /// <returns>The mapped visual bounds.</returns>
    internal virtual ReportRect ResolveObjectVisualBounds(CoreObjectGeometry item, ReportRect bounds, ReportSheet sheet) => bounds;
}
