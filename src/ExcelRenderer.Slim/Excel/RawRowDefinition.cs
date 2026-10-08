namespace ExcelRenderer.Slim.Excel;

/// <summary>A row override read directly from worksheet XML.</summary>
/// <param name="Index">The one-based row index.</param>
/// <param name="Height">The row height in points, or null when the default height applies.</param>
/// <param name="Hidden">Whether the row is hidden.</param>
internal sealed record RawRowDefinition(int Index, double? Height, bool Hidden);
