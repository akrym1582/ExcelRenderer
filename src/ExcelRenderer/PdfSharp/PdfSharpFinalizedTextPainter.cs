using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using PdfSharp.Drawing;

namespace ExcelRenderer.PdfSharp;

/// <summary>Invokes the PDF path that consumes finalized line and run placement.</summary>
/// <param name="paint">The paint value.</param>
internal sealed class PdfSharpFinalizedTextPainter(Action<XGraphics, DrawTextCommand, TextLayoutResult> paint)
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <param name="graphics">The graphics value.</param>
    /// <param name="command">The command value.</param>
    /// <param name="layout">The layout value.</param>
    internal void Paint(XGraphics graphics, DrawTextCommand command, TextLayoutResult layout) =>
        paint(graphics, command, layout);
}
