using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Drawing;

/// <summary>Resolves the effective size shared by finalized text backends.</summary>
internal static class TextLayoutFontSize
{
    /// <summary>Returns the explicit finalized size, or the command style size for a legacy layout.</summary>
    /// <param name="layout">The finalized or legacy layout.</param>
    /// <param name="style">The command font style used by the legacy fallback.</param>
    /// <returns>The size to pass to the drawing backend.</returns>
    internal static double Resolve(TextLayoutResult layout, FontStyle style)
        => Resolve(layout.HasExplicitEffectiveFontSize, layout.EffectiveFontSize, style.Size);

    /// <summary>Resolves product-independent finalized font size.</summary>
    /// <param name="hasExplicitSize">Whether the layout contains a finalized size.</param>
    /// <param name="effectiveSize">The finalized size.</param>
    /// <param name="styleSize">The legacy style size.</param>
    /// <returns>The validated drawing size.</returns>
    internal static double Resolve(bool hasExplicitSize, double effectiveSize, double styleSize)
    {
        var size = hasExplicitSize ? effectiveSize : styleSize;
        if (!double.IsFinite(size) || size < 0)
        {
            throw new ArgumentOutOfRangeException("style", "Font size must be finite and non-negative.");
        }

        return size;
    }
}
