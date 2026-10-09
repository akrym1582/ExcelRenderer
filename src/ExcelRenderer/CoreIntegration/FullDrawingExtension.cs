using ExcelRenderer.Drawing;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Adds transformed images and shapes in the existing shared Z stage.</summary>
internal sealed class FullDrawingExtension : Core.Extensibility.ICoreDrawingExtension
{
    /// <inheritdoc/>
    public bool RequiresTextLayout => false;

    /// <inheritdoc/>
    public IEnumerable<Core.Drawing.DrawCommand> CreateObjectCommands(Core.Layout.RenderPage page)
    {
        var images = (page.Images ?? []).Select(image =>
        {
            var source = (image.ExtensionData as FullRenderImageData)?.Image;
            return (Z: image.ZIndex, Command: (DrawCommand)new DrawImageCommand(page.Number, CoreModelAdapter.ToPublic(image.Bounds), image.ImageBytes)
            {
                Crop = source?.Crop,
                Rotation = source?.Rotation ?? 0,
                FlipHorizontal = source?.FlipHorizontal ?? false,
                FlipVertical = source?.FlipVertical ?? false,
                ClipBounds = source?.ClipBounds,
            });
        });
        var shapes = (page.ExtensionData as FullPageData)?.Shapes ?? [];
        return images.Concat(shapes.Select(shape => (Z: shape.Shape.ZIndex, Command: (DrawCommand)new DrawShapeCommand(page.Number, shape.Bounds, shape.Shape) { ClipBounds = shape.ClipBounds })))
            .OrderBy(item => item.Z).Select(item => CoreCommandAdapter.ToCore(item.Command));
    }
}
