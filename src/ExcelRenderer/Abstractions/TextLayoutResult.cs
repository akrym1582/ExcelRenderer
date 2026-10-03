namespace ExcelRenderer.Abstractions;

/// <summary>Represents finalized text lines and resolved font runs.</summary>
/// <param name="Size">Overall text dimensions.</param>
/// <param name="Lines">Finalized lines.</param>
public sealed record TextLayoutResult(TextSize Size, IReadOnlyList<TextLayoutLine> Lines)
{
    /// <summary>Gets the finalized effective font size, or zero for the compatibility path.</summary>
    public double EffectiveFontSize { get; init; }
}
