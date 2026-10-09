using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Shares original object rectangles and band indices across page-local builders.</summary>
internal sealed class SheetObjectLayoutIndex
{
    /// <summary>Initializes a new instance of the <see cref="SheetObjectLayoutIndex"/> class.</summary>
    /// <param name="context">The planned geometry; the index does not retain the context.</param>
    internal SheetObjectLayoutIndex(ReportLayoutContext context)
    {
        CoreIndex = new(CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context));
        Images = CoreIndex.Objects.Where(item => item.Image is not null).Select(item => (CoreIntegration.CoreModelAdapter.ToPublicImage(item.Image!), CoreIntegration.CoreModelAdapter.ToPublic(item.Bounds), CoreIntegration.CoreModelAdapter.ToPublic(item.Visual))).ToArray();
        Shapes = CoreIndex.Objects.Where(item => item.ExtensionData is CoreIntegration.FullShapeData).Select(item => (((CoreIntegration.FullShapeData)item.ExtensionData!).Shape, CoreIntegration.CoreModelAdapter.ToPublic(item.Bounds), CoreIntegration.CoreModelAdapter.ToPublic(item.Visual))).ToArray();
        ImageBands = new(Images.Select((image, index) => (index, image.Visual.Y, image.Visual.Y + image.Visual.Height)));
        ShapeBands = new(Shapes.Select((shape, index) => (index, shape.Visual.Y, shape.Visual.Y + shape.Visual.Height)));
    }

    /// <summary>Gets the immutable common object index.</summary>
    internal Core.Layout.SheetObjectLayoutIndex CoreIndex { get; }

    /// <summary>Gets immutable positioned source-image references in original order.</summary>
    internal (ReportImage Image, ReportRect Bounds, ReportRect Visual)[] Images { get; }

    /// <summary>Gets immutable positioned source-shape references in original order.</summary>
    internal (ReportShape Shape, ReportRect Bounds, ReportRect Visual)[] Shapes { get; }

    /// <summary>Gets the shared image visual-band index.</summary>
    internal BandIndex<int> ImageBands { get; }

    /// <summary>Gets the shared shape visual-band index.</summary>
    internal BandIndex<int> ShapeBands { get; }
}
