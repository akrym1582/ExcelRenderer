namespace ExcelRenderer.Layout;

/// <summary>Identifies a half-open source interval assigned to a page.</summary>
/// <param name="Start">The Start value.</param>
/// <param name="End">The End value.</param>
/// <returns>The calculated value.</returns>
internal readonly record struct PageBand(double Start, double End);
