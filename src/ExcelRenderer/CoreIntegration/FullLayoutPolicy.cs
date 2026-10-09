using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using CoreContext = ExcelRenderer.Core.Layout.ReportLayoutContext;
using CoreObjectGeometry = ExcelRenderer.Core.Layout.CoreObjectGeometry;
using CorePageBuildContext = ExcelRenderer.Core.Layout.CorePageBuildContext;
using CoreRect = ExcelRenderer.Core.Layout.ReportRect;
using CoreSheet = ExcelRenderer.Core.Model.ReportSheet;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Supplies the full product's geometry, selection, transformed objects and source regions.</summary>
internal sealed class FullLayoutPolicy : Core.Layout.CoreLayoutPolicy
{
    /// <inheritdoc/>
    internal override double ResolveAxisPosition(Core.Layout.ReportLayoutContext context, int index, bool column, double position) =>
        context.Sheet.RequestedRange is null ? position : column ? context.Geometry.ColumnStart(index) : context.Geometry.RowStart(index);

    /// <inheritdoc/>
    internal override IEnumerable<Core.Abstractions.IReportLayoutPass> GetGeometryPasses() => base.GetGeometryPasses().Concat([new FullExplicitRangeGeometryPass()]);

    /// <inheritdoc/>
    internal override Core.Model.CellRange? ResolveUsedRange(CoreContext context)
    {
        if (context.Sheet.RenderUsedRange is { } preserved)
        {
            return preserved;
        }

        var range = base.ResolveUsedRange(context);
        if (range is not null)
        {
            return range;
        }

        var links = Data(context.Sheet)?.Hyperlinks?.Links.Where(link => link.SourceRange != default).Select(link => link.SourceRange).ToArray() ?? [];
        return links.Length == 0 ? null : new(new(links.Min(link => link.First.Row), links.Min(link => link.First.Column)), new(links.Max(link => link.Last.Row), links.Max(link => link.Last.Column)));
    }

    /// <inheritdoc/>
    internal override IReadOnlyList<CoreObjectGeometry> GetObjectGeometry(CoreContext context)
    {
        var result = new List<CoreObjectGeometry>();
        foreach (var image in context.Sheet.Images ?? [])
        {
            var bounds = Core.Layout.ObjectGeometry.GetSheetRect(context.Geometry, image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, image.DrawingAnchor);
            var full = CoreModelAdapter.ToPublicImage(image);
            result.Add(new(Anchor(image.Anchor, image.DrawingAnchor, bounds), bounds, Core.Layout.ObjectGeometry.GetVisualBounds(bounds, full.Rotation), image));
        }

        foreach (var shape in Data(context.Sheet)?.Shapes ?? [])
        {
            var anchor = shape.DrawingAnchor is { } source ? CoreModelAdapter.ToCore(source) : null;
            var bounds = Core.Layout.ObjectGeometry.GetSheetRect(context.Geometry, CoreModelAdapter.ToCore(shape.Anchor), shape.OffsetX, shape.OffsetY, shape.Width, shape.Height, anchor);
            result.Add(new(Anchor(CoreModelAdapter.ToCore(shape.Anchor), anchor, bounds), bounds, Core.Layout.ObjectGeometry.GetVisualBounds(bounds, shape.Rotation), null, new FullShapeData(shape)));
        }

        return result;

        Core.Model.CellAddress Anchor(Core.Model.CellAddress fallback, Core.Model.DrawingAnchor? anchor, CoreRect bounds) => anchor?.Kind == Core.Model.DrawingAnchorKind.Absolute
            ? new(context.Geometry.RowAt(bounds.Y), context.Geometry.ColumnAt(bounds.X)) : anchor?.From ?? fallback;
    }

    /// <inheritdoc/>
    internal override Core.Layout.PageCellSelection CreateCellSelection(Core.Layout.PageBand horizontal, Core.Layout.PageBand vertical, HashSet<int> titleColumns, HashSet<int> titleRows, double titleColumnEnd, double titleRowEnd, CoreSheet sheet) =>
        new FullCellSelection(horizontal, vertical, titleColumns, titleRows, titleColumnEnd, titleRowEnd, sheet.RequestedRange is not null);

    /// <inheritdoc/>
    internal override bool HasPageContent(CoreContext context) => context.Sheet.RequestedRange is not null || (Data(context.Sheet)?.Hyperlinks?.Links.Count ?? 0) > 0 || (Data(context.Sheet)?.Shapes.Count ?? 0) > 0 || base.HasPageContent(context);

    /// <inheritdoc/>
    internal override IEnumerable<KeyValuePair<Core.Model.CellAddress, Core.Model.ReportCell>> GetPaginationCells(CoreContext context) => context.Sheet.RequestedRange is null ? base.GetPaginationCells(context) : [];

    /// <inheritdoc/>
    internal override IEnumerable<KeyValuePair<Core.Model.CellAddress, Core.Model.ReportCell>> SelectSheetCells(CoreContext context)
    {
        var cells = base.SelectSheetCells(context);
        return context.Sheet.RequestedRange is not { } selected ? cells : cells.Where(entry => selected.Contains(entry.Key) || context.Sheet.MergedRanges.Any(range => range.First == entry.Key && range.First.Row <= selected.Last.Row && range.Last.Row >= selected.First.Row && range.First.Column <= selected.Last.Column && range.Last.Column >= selected.First.Column));
    }

    /// <inheritdoc/>
    internal override string PrepareMeasurementText(string text, Core.Model.CellStyle style)
    {
        if (!style.TopToBottom && style.TextRotation != 255)
        {
            return text;
        }

        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        var result = new List<string>();
        while (elements.MoveNext())
        {
            result.Add(elements.GetTextElement());
        }

        return string.Join("\n", result);
    }

    /// <inheritdoc/>
    internal override CoreRect ResolveObjectVisualBounds(CoreObjectGeometry item, CoreRect bounds, CoreSheet sheet) => item.Image is { } image
        ? Core.Layout.ObjectGeometry.GetVisualBounds(bounds, CoreModelAdapter.ToPublicImage(image).Rotation)
        : item.ExtensionData is FullShapeData data ? CoreModelAdapter.ToCore(sheet.RequestedRange is null
            ? Layout.ObjectGeometry.GetVisualBounds(CoreModelAdapter.ToPublic(bounds), data.Shape.Rotation)
            : Layout.ObjectGeometry.GetShapeVisualBounds(CoreModelAdapter.ToPublic(bounds), data.Shape))
        : throw new InvalidOperationException("Full object geometry metadata is missing.");

    /// <inheritdoc/>
    internal override IReadOnlyList<Core.Layout.RenderImage> BuildPageImages(CorePageBuildContext context)
    {
        var result = new List<Core.Layout.RenderImage>();
        var objects = context.Layout.ObjectLayouts!;
        foreach (var index in objects.Bands.Query(context.Vertical.Start, context.Vertical.End).OrderBy(index => index))
        {
            var item = objects.Objects[index];
            if (item.Image is not { } image || !Intersects(item.Visual, context.Horizontal, context.Vertical))
            {
                continue;
            }

            var full = CoreModelAdapter.ToPublicImage(image);
            var bounds = context.Placement.MapBodyObjectBounds(item.Bounds);
            var rendered = new RenderImage(CoreModelAdapter.ToPublic(bounds), image.ImageBytes, image.ZIndex)
            {
                Crop = full.Crop,
                Rotation = full.Rotation,
                FlipHorizontal = full.FlipHorizontal,
                FlipVertical = full.FlipVertical,
                ClipBounds = CoreModelAdapter.ToPublic(context.Placement.BodyClip),
            };
            result.Add(new(bounds, image.ImageBytes, image.ZIndex) { ExtensionData = new FullRenderImageData(rendered) });
        }

        return result;
    }

    /// <inheritdoc/>
    internal override Core.Layout.RenderPage BuildAdditionalPageContent(CorePageBuildContext context, Core.Layout.RenderPage page)
    {
        var regions = BuildRegions(context);
        var shapes = new List<RenderShape>();
        var objects = context.Layout.ObjectLayouts!;
        foreach (var index in objects.Bands.Query(context.Vertical.Start, context.Vertical.End).OrderBy(index => index))
        {
            var item = objects.Objects[index];
            if (item.ExtensionData is not FullShapeData data || !Intersects(item.Visual, context.Horizontal, context.Vertical))
            {
                continue;
            }

            shapes.Add(new(CoreModelAdapter.ToPublic(context.Placement.MapBodyObjectBounds(item.Bounds)), ScaleShape(data.Shape, context.Placement.Scale))
            {
                ClipBounds = CoreModelAdapter.ToPublic(context.Placement.BodyClip),
            });
        }

        return page with
        {
            Cells = context.Layout.Sheet.RequestedRange is null ? page.Cells : page.Cells.Select(cell =>
            {
                var address = cell.SourceAddress!.Value;
                var source = Core.Layout.RectangleGeometry.Bounds(context.Layout.Geometry, new(address, new(address.Row + cell.Cell.RowSpan - 1, address.Column + cell.Cell.ColumnSpan - 1)));
                var publicSource = CoreModelAdapter.ToPublic(source);
                var clip = regions.FirstOrDefault(region => Math.Abs(region.Map(publicSource).X - cell.Bounds.X) < 1e-7 && Math.Abs(region.Map(publicSource).Y - cell.Bounds.Y) < 1e-7)?.PageBounds ?? CoreModelAdapter.ToPublic(context.Placement.BodyClip);
                return cell with { ClipBounds = CoreModelAdapter.ToCore(clip) };
            }).ToArray(),
            ExtensionData = new FullPageData(shapes, regions),
        };
    }

    private static FullSheetData? Data(CoreSheet sheet) => sheet.ExtensionData as FullSheetData;

    private static bool Intersects(CoreRect bounds, Core.Layout.PageBand horizontal, Core.Layout.PageBand vertical) => bounds.X < horizontal.End && bounds.X + bounds.Width > horizontal.Start && bounds.Y < vertical.End && bounds.Y + bounds.Height > vertical.Start;

    private static IEnumerable<int> QueryStarts(IReadOnlyList<int> indices, Func<int, double> start, double minimum, double maximum)
    {
        var low = 0;
        var high = indices.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (start(indices[middle]) < minimum)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var index = low; index < indices.Count && start(indices[index]) < maximum; index++)
        {
            yield return indices[index];
        }
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

    private static IReadOnlyList<PageSourceRegion> BuildRegions(CorePageBuildContext context)
    {
        var placement = context.Placement;
        var horizontal = context.Horizontal;
        var vertical = context.Vertical;
        var repeatColumns = context.RepeatColumns;
        var repeatRows = context.RepeatRows;
        var layout = context.Layout;
        var columns = QueryStarts(context.BodyColumns, column => layout.ColumnLayouts[column].X, horizontal.Start, horizontal.End).ToArray();
        var rows = QueryStarts(context.BodyRows, row => layout.RowLayouts[row].Y, vertical.Start, vertical.End).ToArray();
        var regions = new List<PageSourceRegion>();
        Add(columns, rows, false, false);
        if (repeatColumns)
        {
            Add(context.TitleColumns, rows, true, false);
        }

        if (repeatRows)
        {
            Add(columns, context.TitleRows, false, true);
        }

        if (repeatColumns && repeatRows)
        {
            Add(context.TitleColumns, context.TitleRows, true, true);
        }

        return regions;

        void Add(IReadOnlyList<int> cs, IReadOnlyList<int> rs, bool titleColumn, bool titleRow)
        {
            if (cs.Count == 0 || rs.Count == 0)
            {
                return;
            }

            var source = Core.Layout.RectangleGeometry.Bounds(layout.Geometry, new(new(rs[0], cs[0]), new(rs[rs.Count - 1], cs[cs.Count - 1])));
            var x = placement.MapCellX(
                layout.ColumnLayouts[cs[0]].X,
                repeatColumns,
                titleColumn,
                context.TitleColumns.Count == 0 ? 0 : layout.ColumnLayouts[context.TitleColumns[0]].X);
            var y = placement.MapCellY(
                layout.RowLayouts[rs[0]].Y,
                repeatRows,
                titleRow,
                context.TitleRows.Count == 0 ? 0 : layout.RowLayouts[context.TitleRows[0]].Y);
            regions.Add(new(CoreModelAdapter.ToPublic(source), new(x, y, source.Width * placement.Scale, source.Height * placement.Scale), placement.Scale, titleColumn || titleRow) { Cells = new(new(rs[0], cs[0]), new(rs[rs.Count - 1], cs[cs.Count - 1])) });
        }
    }
}
