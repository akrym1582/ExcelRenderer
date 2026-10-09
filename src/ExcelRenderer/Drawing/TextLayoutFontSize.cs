using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>Resolves the effective size shared by finalized text backends.</summary>
internal static class TextLayoutFontSize
{
    /// <summary>Returns the explicit finalized size, or the command style size for a legacy layout.</summary>
    /// <param name="layout">The finalized or legacy layout.</param>
    /// <param name="style">The command font style used by the legacy fallback.</param>
    /// <returns>The size to pass to the drawing backend.</returns>
    internal static double Resolve(TextLayoutResult layout, FontStyle style)
        => Core.Drawing.TextLayoutFontSize.Resolve(layout.HasExplicitEffectiveFontSize, layout.EffectiveFontSize, style.Size);
}
