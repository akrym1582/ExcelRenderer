using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

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
            var source = ObjectGeometry.GetSheetRect(context.Geometry, image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, image.DrawingAnchor);
            if (context.PrintArea is { } area)
            {
                var printed = RectangleGeometry.Bounds(context.Geometry, area);
                if (source.X < printed.X || source.X >= printed.X + printed.Width ||
                    source.Y < printed.Y || source.Y >= printed.Y + printed.Height)
                {
                    continue;
                }
            }

            if (DrawingAnchorResolver.TryResolve(
                context, image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, image.DrawingAnchor, out var bounds))
            {
                images.Add((image, bounds, bounds));
            }
        }

        Images = images.ToArray();
        ImageBands = new(Images.Select((image, index) => (index, image.Visual.Y, image.Visual.Y + image.Visual.Height)));
    }

    /// <summary>Gets immutable positioned source-image references in original order.</summary>
    internal (ReportImage Image, ReportRect Bounds, ReportRect Visual)[] Images { get; }

    /// <summary>Gets the shared image visual-band index.</summary>
    internal BandIndex<int> ImageBands { get; }
}
