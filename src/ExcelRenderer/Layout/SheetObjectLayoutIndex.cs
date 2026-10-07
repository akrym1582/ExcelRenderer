using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Shares original object rectangles and band indices across page-local builders.</summary>
internal sealed class SheetObjectLayoutIndex
{
    /// <summary>Initializes a new instance of the <see cref="SheetObjectLayoutIndex"/> class.</summary>
    /// <param name="context">The planned geometry; the index does not retain the context.</param>
    internal SheetObjectLayoutIndex(ReportLayoutContext context)
    {
        var images = new List<(ReportImage Image, ReportRect Bounds, ReportRect Visual)>();
        foreach (var image in context.Sheet.Images ?? [])
        {
            if (DrawingAnchorResolver.TryResolve(
                context, image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, image.DrawingAnchor, out var bounds))
            {
                images.Add((image, bounds, ObjectGeometry.GetVisualBounds(bounds, image.Rotation)));
            }
        }

        var shapes = new List<(ReportShape Shape, ReportRect Bounds, ReportRect Visual)>();
        foreach (var shape in context.Sheet.Shapes ?? [])
        {
            if (DrawingAnchorResolver.TryResolve(
                context, shape.Anchor, shape.OffsetX, shape.OffsetY, shape.Width, shape.Height, shape.DrawingAnchor, out var bounds))
            {
                shapes.Add((shape, bounds, context.Sheet.RequestedRange is null
                    ? ObjectGeometry.GetVisualBounds(bounds, shape.Rotation)
                    : ObjectGeometry.GetShapeVisualBounds(bounds, shape)));
            }
        }

        Images = images.ToArray();
        Shapes = shapes.ToArray();
        ImageBands = new(Images.Select((image, index) => (index, image.Visual.Y, image.Visual.Y + image.Visual.Height)));
        ShapeBands = new(Shapes.Select((shape, index) => (index, shape.Visual.Y, shape.Visual.Y + shape.Visual.Height)));
    }

    /// <summary>Gets immutable positioned source-image references in original order.</summary>
    internal (ReportImage Image, ReportRect Bounds, ReportRect Visual)[] Images { get; }

    /// <summary>Gets immutable positioned source-shape references in original order.</summary>
    internal (ReportShape Shape, ReportRect Bounds, ReportRect Visual)[] Shapes { get; }

    /// <summary>Gets the shared image visual-band index.</summary>
    internal BandIndex<int> ImageBands { get; }

    /// <summary>Gets the shared shape visual-band index.</summary>
    internal BandIndex<int> ShapeBands { get; }
}
