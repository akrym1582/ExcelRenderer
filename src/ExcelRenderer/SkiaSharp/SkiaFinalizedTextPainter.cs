using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Draws finalized Skia lines at stored point-coordinate baselines and run offsets.</summary>
internal sealed class SkiaFinalizedTextPainter
{
    private readonly SkiaTextDrawing _drawing;

    /// <summary>Initializes a new instance of the <see cref="SkiaFinalizedTextPainter"/> class.</summary>
    /// <param name="drawing">The backend run and resource owner.</param>
    internal SkiaFinalizedTextPainter(SkiaTextDrawing drawing) => _drawing = drawing;

    /// <summary>Paints a finalized layout without rewrapping, shrinking, or resolving its runs.</summary>
    /// <param name="canvas">The caller-owned canvas.</param>
    /// <param name="command">The finalized text command.</param>
    /// <param name="layout">The finalized point-coordinate layout.</param>
    internal void Paint(SKCanvas canvas, DrawTextCommand command, TextLayoutResult layout) =>
        _drawing.PaintFinalized(canvas, command, layout);
}
