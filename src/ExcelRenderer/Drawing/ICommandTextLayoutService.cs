using ExcelRenderer.Abstractions;

namespace ExcelRenderer.Drawing;

/// <summary>Preserves a compatibility backend's native measurement policy at the common finalization boundary.</summary>
internal interface ICommandTextLayoutService : ITextLayoutService
{
    /// <summary>Resolves an unfinalized command, including its existing shrink and native rounding policy.</summary>
    /// <param name="command">The original command in points.</param>
    /// <returns>A handle-free immutable layout.</returns>
    TextLayoutResult Layout(DrawTextCommand command);
}
