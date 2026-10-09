using ExcelRenderer.Abstractions;

namespace ExcelRenderer.Layout;

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
            layout.Lines is System.Collections.ObjectModel.ReadOnlyCollection<TextLayoutLine> &&
            layout.Lines.All(line => line.Runs is System.Collections.ObjectModel.ReadOnlyCollection<TextLayoutRun>))
        {
            return layout;
        }

        return CoreIntegration.CoreTextLayoutAdapter.ToPublic(Core.Layout.TextLayoutTransform.Scale(CoreIntegration.CoreTextLayoutAdapter.ToCore(layout), factor));
    }
}
