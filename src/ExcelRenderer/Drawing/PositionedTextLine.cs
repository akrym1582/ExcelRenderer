using ExcelRenderer.Abstractions;

namespace ExcelRenderer.Drawing;

/// <summary>A finalized line placed in command coordinates.</summary>
/// <param name="Line">The finalized line.</param>
/// <param name="Left">The absolute line origin.</param>
/// <param name="Baseline">The absolute baseline.</param>
internal sealed record PositionedTextLine(TextLayoutLine Line, double Left, double Baseline);
