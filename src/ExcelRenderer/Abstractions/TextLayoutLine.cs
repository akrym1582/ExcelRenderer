namespace ExcelRenderer.Abstractions;

/// <summary>Represents one finalized text line.</summary>
/// <param name="Text">Text contained by the line.</param>
/// <param name="Width">Line advance in points.</param>
/// <param name="Height">Line height in points.</param>
/// <param name="Baseline">Baseline offset from the line top.</param>
/// <param name="Runs">Resolved font runs in drawing order.</param>
/// <param name="ExplicitBreak">Whether an explicit newline ended this line.</param>
public sealed record TextLayoutLine(
    string Text,
    double Width,
    double Height,
    double Baseline,
    IReadOnlyList<TextLayoutRun> Runs,
    bool ExplicitBreak);
