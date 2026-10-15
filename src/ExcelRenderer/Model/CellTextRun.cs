namespace ExcelRenderer.Model;

/// <summary>Represents text with effective partial cell formatting.</summary>
/// <param name="Text">The text of the run.</param>
/// <param name="Font">The effective font with inherited properties resolved.</param>
public sealed record CellTextRun(string Text, FontStyle Font);
