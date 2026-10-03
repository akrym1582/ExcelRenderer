using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using PdfSharp.Drawing;

namespace ExcelRenderer.PdfSharp;

/// <summary>Invokes the PDF compatibility path that performs wrapping and shrinking.</summary>
/// <param name="paint">The paint value.</param>
internal sealed class PdfSharpLegacyTextPainter(Action<XGraphics, DrawTextCommand> paint)
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <param name="graphics">The graphics value.</param>
    /// <param name="command">The command value.</param>
    internal void Paint(XGraphics graphics, DrawTextCommand command) => paint(graphics, command);
}
