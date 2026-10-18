using ExcelRenderer.Core.Drawing;
using SkiaSharp;

namespace ExcelRenderer.Core.Extensibility;

/// <summary>Paints finalized text without repeating layout or font resolution.</summary>
internal interface ICorePdfTextPainter
{
    /// <summary>Paints a single text command.</summary>
    /// <param name="graphics">The page graphics owned by the renderer.</param>
    /// <param name="command">The finalized command.</param>
    void Paint(SKCanvas graphics, DrawTextCommand command);
}
