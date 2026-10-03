using ExcelRenderer.Fonts;

namespace ExcelRenderer.Abstractions;

/// <summary>Represents a resolved font run and its horizontal placement.</summary>
/// <param name="Run">Resolved text and font information.</param>
/// <param name="X">Horizontal offset from the line origin.</param>
/// <param name="Advance">Logical run advance in points; this is not an ink bounding-box width.</param>
public sealed record TextLayoutRun(TextRun Run, double X, double Advance);
