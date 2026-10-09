namespace ExcelRenderer.Core.Abstractions;

/// <summary>Represents one finalized text line.</summary>
/// <param name="Text">Text contained by the line.</param>
/// <param name="Width">Line advance in points.</param>
/// <param name="Height">Line height in points.</param>
/// <param name="Baseline">Baseline offset from the line top.</param>
/// <param name="ExplicitBreak">Whether an explicit newline ended this line.</param>
internal sealed record TextLayoutLine(
    string Text,
    double Width,
    double Height,
    double Baseline,
    bool ExplicitBreak)
{
    /// <summary>Gets the maximum distance above the baseline.</summary>
    public double Ascent { get; init; }

    /// <summary>Gets the maximum distance below the baseline.</summary>
    public double Descent { get; init; }

    /// <summary>Gets the additional inter-line spacing.</summary>
    public double Leading { get; init; }

    /// <summary>Gets borrowed product line metadata.</summary>
    internal Extensibility.ICoreExtensionData? ExtensionData { get; init; }

    /// <summary>Gets the accumulated geometric scale for borrowed run placement.</summary>
    internal double ExtensionScale { get; init; } = 1;
}
