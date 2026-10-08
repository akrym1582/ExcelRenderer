using ExcelRenderer.Slim.Abstractions;

namespace ExcelRenderer.Slim.Layout;

/// <summary>Provides non-mutating transformations for finalized text geometry.</summary>
internal static class TextLayoutTransform
{
    /// <summary>Scales all finalized geometry while preserving an unspecified effective font size.</summary>
    /// <param name="layout">The finalized layout to scale.</param>
    /// <param name="factor">A finite, non-negative scale factor.</param>
    /// <returns>A new scaled layout.</returns>
    internal static TextLayoutResult Scale(TextLayoutResult layout, double factor)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (!double.IsFinite(factor) || factor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "Scale must be finite and non-negative.");
        }

        if (factor == 1 &&
            layout.Lines is System.Collections.ObjectModel.ReadOnlyCollection<TextLayoutLine>)
        {
            return layout;
        }

        var scaled = new TextLayoutResult(
            new(layout.Size.Width * factor, layout.Size.Height * factor),
            layout.Lines.Select(line => line with
            {
                Width = line.Width * factor,
                Height = line.Height * factor,
                Baseline = line.Baseline * factor,
                Ascent = line.Ascent * factor,
                Descent = line.Descent * factor,
                Leading = line.Leading * factor,
            }).ToArray());
        return layout.HasExplicitEffectiveFontSize
            ? scaled with { EffectiveFontSize = layout.EffectiveFontSize * factor }
            : scaled;
    }
}
