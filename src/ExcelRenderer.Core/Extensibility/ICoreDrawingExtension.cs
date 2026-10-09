using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Layout;

namespace ExcelRenderer.Core.Extensibility;

/// <summary>Supplies product drawing policy without exposing product models to Core.</summary>
internal interface ICoreDrawingExtension
{
    /// <summary>Gets a value indicating whether cell text must already have finalized geometry.</summary>
    bool RequiresTextLayout { get; }

    /// <summary>Creates the shared image/object stage in product Z order.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The ordered object commands.</returns>
    IEnumerable<DrawCommand> CreateObjectCommands(RenderPage page);
}
