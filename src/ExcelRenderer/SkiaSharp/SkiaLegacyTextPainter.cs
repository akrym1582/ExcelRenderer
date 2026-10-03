using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Invokes the Skia compatibility path that performs wrapping and shrinking.</summary>
/// <param name="paint">The paint value.</param>
internal sealed class SkiaLegacyTextPainter(Action<SKCanvas, DrawTextCommand> paint)
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <param name="canvas">The canvas value.</param>
    /// <param name="command">The command value.</param>
    internal void Paint(SKCanvas canvas, DrawTextCommand command) => paint(canvas, command);
}
