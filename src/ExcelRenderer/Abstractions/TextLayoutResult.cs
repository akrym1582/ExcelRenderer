namespace ExcelRenderer.Abstractions;

/// <summary>Represents finalized text lines and resolved font runs.</summary>
/// <param name="Size">Overall text dimensions.</param>
/// <param name="Lines">Finalized lines.</param>
public sealed record TextLayoutResult(TextSize Size, IReadOnlyList<TextLayoutLine> Lines);
