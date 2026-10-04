using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;

namespace ExcelRenderer.Rendering;

/// <summary>Final output translation and dimensions for an already paginated page.</summary>
/// <param name="OriginalWidth">The original page width.</param>
/// <param name="OriginalHeight">The original page height.</param>
/// <param name="Crop">The clipped visible content rectangle.</param>
/// <param name="Padding">The padding on each side.</param>
internal sealed record PageViewport(double OriginalWidth, double OriginalHeight, ReportRect Crop, double Padding)
{
    /// <summary>Gets the output width.</summary>
    internal double Width => Crop.Width + (Padding * 2);

    /// <summary>Gets the output height.</summary>
    internal double Height => Crop.Height + (Padding * 2);

    /// <summary>Clips and translates finalized drawing commands without changing their layout.</summary>
    /// <param name="commands">The original drawing commands.</param>
    /// <param name="pageNumber">The original page number.</param>
    /// <returns>The common viewport drawing command.</returns>
    internal DrawCommand Apply(IReadOnlyList<DrawCommand> commands, int pageNumber) =>
        new DrawViewportCommand(pageNumber, commands, Crop, -Crop.X + Padding, -Crop.Y + Padding);

    /// <summary>Transforms a page-space rectangle into output coordinates.</summary>
    /// <param name="rect">The original rectangle.</param>
    /// <returns>The translated rectangle.</returns>
    internal ReportRect Map(ReportRect rect) => rect with { X = rect.X - Crop.X + Padding, Y = rect.Y - Crop.Y + Padding };
}
