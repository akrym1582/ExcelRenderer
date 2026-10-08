using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Drawing;

/// <summary>Resolves the effective size shared by finalized text backends.</summary>
internal static class TextLayoutFontSize
{
    /// <summary>Returns the explicit finalized size, or the command style size for a legacy layout.</summary>
    /// <param name="layout">The finalized or legacy layout.</param>
    /// <param name="style">The command font style used by the legacy fallback.</param>
    /// <returns>The size to pass to the drawing backend.</returns>
    internal static double Resolve(TextLayoutResult layout, FontStyle style)
    {
        var size = layout.HasExplicitEffectiveFontSize ? layout.EffectiveFontSize : style.Size;
        if (!double.IsFinite(size) || size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(style), "Font size must be finite and non-negative.");
        }

        return size;
    }
}
