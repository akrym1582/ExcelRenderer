using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Layout;

namespace ExcelRenderer.Core.Extensibility;

/// <summary>Supplies product drawing policy without exposing product models to Core.</summary>
internal interface ICoreDrawingExtension
{
    /// <summary>Gets a value indicating whether cell text must already have finalized geometry.</summary>
    bool RequiresTextLayout { get; }

    /// <summary>Creates product cell decorations between backgrounds and borders.</summary>
    /// <param name="pageNumber">Output page.</param>
    /// <param name="cell">Placed cell.</param>
    /// <returns>The ordered decoration commands.</returns>
    IEnumerable<DrawCommand> CreateCellDecorationCommands(int pageNumber, RenderCell cell);

    /// <summary>Determines whether the placed cell text is visible.</summary>
    /// <param name="cell">Placed cell.</param>
    /// <returns>Whether text is emitted.</returns>
    bool ShouldDrawCellText(RenderCell cell);

    /// <summary>Creates the shared image/object stage in product Z order.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The ordered object commands.</returns>
    IEnumerable<DrawCommand> CreateObjectCommands(RenderPage page);
}
