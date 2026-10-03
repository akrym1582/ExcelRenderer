namespace ExcelRenderer.Abstractions;

/// <summary>Represents finalized text lines and resolved font runs.</summary>
/// <param name="Size">Overall advance width and the sum of all line heights.</param>
/// <param name="Lines">Finalized lines.</param>
public sealed record TextLayoutResult(TextSize Size, IReadOnlyList<TextLayoutLine> Lines)
{
    private double? _effectiveFontSize;

    /// <summary>Gets the finalized effective font size, or zero when legacy callers did not specify one.</summary>
    public double EffectiveFontSize
    {
        get => _effectiveFontSize ?? 0;
        init
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Effective font size must be finite and non-negative.");
            }

            _effectiveFontSize = value;
        }
    }

    /// <summary>Gets a value indicating whether an effective size was explicitly finalized.</summary>
    internal bool HasExplicitEffectiveFontSize => _effectiveFontSize.HasValue;
}
