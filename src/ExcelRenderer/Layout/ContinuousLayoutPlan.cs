using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Plans canvas geometry without retaining text layouts, render cells, or commands.</summary>
internal sealed class ContinuousLayoutPlan
{
    private readonly ReportSheet sheet;
    private readonly SheetGeometry geometry;
    private readonly Dictionary<int, ColumnLayout> columns;
    private readonly Dictionary<int, RowLayout> rows;
    private readonly Core.Layout.ReportLayoutContext coreGeometry;
    private readonly ReportRect? clip;
    private readonly double shiftX;
    private readonly double shiftY;

    /// <summary>Initializes a new instance of the <see cref="ContinuousLayoutPlan"/> class.</summary>
    /// <param name="sheet">The sheet used by this operation.</param>
    /// <param name="measurer">The measurer used by this operation.</param>
    internal ContinuousLayoutPlan(ReportSheet sheet, ITextMeasurer measurer)
    {
        this.sheet = sheet;
        var context = new ReportLayoutContext(sheet, measurer);
        new NormalizePass().Execute(context);
        new ResolvePrintAreaPass { IgnoreExplicitPrintArea = sheet.RequestedRange is null }.Execute(context);
        new HiddenRowColumnPass { IncludePrintTitles = false }.Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new ExplicitRangeGeometryPass().Execute(context);
        geometry = context.Geometry;
        columns = context.ColumnLayouts;
        rows = context.RowLayouts;
        coreGeometry = CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context);
        Images = ContinuousLayoutPass.BuildImages(context);
        Shapes = ContinuousLayoutPass.BuildShapes(context);
        if (sheet.RequestedRange is { } requested)
        {
            clip = RectangleGeometry.Bounds(geometry, requested);
            shiftX = -clip.Value.X;
            shiftY = -clip.Value.Y;
            Width = clip.Value.Width > 0 && clip.Value.Height > 0 ? clip.Value.Width : 1;
            Height = clip.Value.Width > 0 && clip.Value.Height > 0 ? clip.Value.Height : 1;
            SourceRegions = [new(clip.Value, new(0, 0, clip.Value.Width, clip.Value.Height), 1, false) { Cells = requested }];
        }
        else
        {
            var visual = CellBoundsPass.EnumerateBounds(context).SelectMany(cell => new[] { cell.Bounds }.Concat(
                (sheet.Cells[cell.Address].MergedBorders ?? []).Where(border => columns.ContainsKey(border.Address.Column) && rows.ContainsKey(border.Address.Row))
                    .Select(border => new ReportRect(columns[border.Address.Column].X, rows[border.Address.Row].Y, columns[border.Address.Column].Width, rows[border.Address.Row].Height))))
                .Concat(Images.Select(image => ObjectGeometry.GetVisualBounds(image.Bounds, image.Rotation)))
                .Concat(Shapes.Select(shape => ObjectGeometry.GetVisualBounds(shape.Bounds, shape.Shape.Rotation)));
            double minX = 0, minY = 0, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var bounds in visual)
            {
                minX = Math.Min(minX, bounds.X);
                minY = Math.Min(minY, bounds.Y);
                maxX = Math.Max(maxX, bounds.X + bounds.Width);
                maxY = Math.Max(maxY, bounds.Y + bounds.Height);
            }

            shiftX = -minX;
            shiftY = -minY;
            Width = double.IsNegativeInfinity(maxX) ? 1 : Math.Max(1, maxX + shiftX);
            Height = double.IsNegativeInfinity(maxY) ? 1 : Math.Max(1, maxY + shiftY);
        }

        Images = Images.Where(image => clip is null || RectangleGeometry.Intersect(ObjectGeometry.GetVisualBounds(image.Bounds, image.Rotation), clip.Value) is not null)
            .Select(image => image with { Bounds = Move(image.Bounds), ClipBounds = clip is null ? null : Move(clip.Value) }).ToArray();
        Shapes = Shapes.Where(shape => clip is null || RectangleGeometry.Intersect(ObjectGeometry.GetShapeVisualBounds(shape.Bounds, shape.Shape), clip.Value) is not null)
            .Select(shape => shape with { Bounds = Move(shape.Bounds), ClipBounds = clip is null ? null : Move(clip.Value) }).ToArray();
    }

    /// <summary>Gets the final canvas width in points.</summary>
    internal double Width { get; }

    /// <summary>Gets the final canvas height in points.</summary>
    internal double Height { get; }

    /// <summary>Gets source regions used for viewport and hyperlink mapping.</summary>
    internal IReadOnlyList<PageSourceRegion> SourceRegions { get; } = [];

    /// <summary>Gets the lightweight positioned image references.</summary>
    internal IReadOnlyList<RenderImage> Images { get; }

    /// <summary>Gets the lightweight positioned shape references.</summary>
    internal IReadOnlyList<RenderShape> Shapes { get; }

    /// <summary>Enumerates complete render cells for the materializing compatibility API.</summary>
    /// <param name="measurer">The text measurer.</param>
    /// <returns>Complete cells in original model order.</returns>
    internal IEnumerable<RenderCell> EnumerateCells(ITextMeasurer measurer) => EnumerateCore(measurer, null);

    /// <summary>Enumerates only cells needed by the specified layer, measuring text only for the text layer.</summary>
    /// <param name="measurer">The text measurer.</param>
    /// <param name="layer">The drawing layer.</param>
    /// <returns>Current-layer cells in original model order.</returns>
    internal IEnumerable<RenderCell> EnumerateLayerCells(ITextMeasurer measurer, DrawingLayer layer)
    {
        if (layer is not (DrawingLayer.Background or DrawingLayer.CellBorder or DrawingLayer.MergedBorder or DrawingLayer.Text))
        {
            throw new ArgumentOutOfRangeException(nameof(layer));
        }

        return EnumerateCore(measurer, layer);
    }

    private IEnumerable<RenderCell> EnumerateCore(ITextMeasurer measurer, DrawingLayer? layer)
    {
        var context = new Core.Layout.ReportLayoutContext(coreGeometry.Sheet, CoreIntegration.CoreTextMeasurerAdapter.Create(measurer), coreGeometry.Geometry)
        {
            Policy = coreGeometry.Policy,
            ColumnLayouts = coreGeometry.ColumnLayouts,
            RowLayouts = coreGeometry.RowLayouts,
        };

        foreach (var address in sheet.Cells.Keys)
        {
            var source = sheet.Cells[address];
            if ((layer == DrawingLayer.Background && source.Style.Background is null) ||
                (layer == DrawingLayer.CellBorder && source.Style.Border is null) ||
                (layer == DrawingLayer.MergedBorder && (source.MergedBorders?.Count ?? 0) == 0) ||
                (layer == DrawingLayer.Text && string.IsNullOrEmpty(source.Text)))
            {
                continue;
            }

            var coreAddress = CoreIntegration.CoreModelAdapter.ToCore(address);
            context.CandidateAddresses = [coreAddress];
            if (layer is DrawingLayer.Background or DrawingLayer.CellBorder)
            {
                var (_, coreBounds) = Core.Layout.CellBoundsPass.EnumerateBounds(context).FirstOrDefault();
                var bounds = CoreIntegration.CoreModelAdapter.ToPublic(coreBounds);
                if (bounds.Width > 0 && bounds.Height > 0 && (clip is null || RectangleGeometry.Intersect(bounds, clip.Value) is not null))
                {
                    yield return new(source, Move(bounds))
                    {
                        SourceAddress = address,
                        ClipBounds = clip is null ? null : Move(clip.Value),
                    };
                }

                continue;
            }

            context.CellLayouts.Clear();
            context.TextLayouts.Clear();
            context.TextSizes.Clear();
            if (layer is null or DrawingLayer.Text)
            {
                new Core.Layout.TextMeasurePass().Execute(context);
            }

            new Core.Layout.CellBoundsPass().Execute(context);
            if (!context.CellLayouts.TryGetValue(coreAddress, out var cell) ||
                (clip is { } selected && RectangleGeometry.Intersect(CoreIntegration.CoreModelAdapter.ToPublic(cell.Bounds), selected) is null))
            {
                continue;
            }

            yield return new(sheet.Cells[address], Move(CoreIntegration.CoreModelAdapter.ToPublic(cell.Bounds)))
            {
                SourceAddress = address,
                ContentBounds = Move(CoreIntegration.CoreModelAdapter.ToPublic(cell.ContentBounds)),
                TextLayout = context.TextLayouts.TryGetValue(coreAddress, out var text) ? CoreIntegration.CoreTextLayoutAdapter.ToPublic(text) : null,
                MergedBorders = cell.MergedBorders?.Select(border => new RenderBorder(Move(CoreIntegration.CoreModelAdapter.ToPublic(border.Bounds)), CoreIntegration.CoreModelAdapter.ToPublic(border.Border))).ToArray(),
                ClipBounds = clip is null ? null : Move(clip.Value),
            };
        }
    }

    private ReportRect Move(ReportRect rect) => rect with { X = rect.X + shiftX, Y = rect.Y + shiftY };
}
