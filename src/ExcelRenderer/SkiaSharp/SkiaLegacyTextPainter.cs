using ExcelRenderer.Drawing;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Draws compatibility text, including wrapping, shrinking, fallback, IVS, and emoji.</summary>
internal sealed class SkiaLegacyTextPainter
{
    private readonly SkiaTextDrawing _drawing;

    /// <summary>Initializes a new instance of the <see cref="SkiaLegacyTextPainter"/> class.</summary>
    /// <param name="drawing">The backend run and resource owner.</param>
    internal SkiaLegacyTextPainter(SkiaTextDrawing drawing) => _drawing = drawing;

    /// <summary>Measures and paints a command which has no finalized layout.</summary>
    /// <param name="canvas">The caller-owned canvas.</param>
    /// <param name="command">The compatibility text command.</param>
    internal void Paint(SKCanvas canvas, DrawTextCommand command) => _drawing.PaintLegacy(canvas, command);
}
