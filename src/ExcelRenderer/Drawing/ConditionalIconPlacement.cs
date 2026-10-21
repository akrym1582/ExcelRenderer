using ExcelRenderer.CoreIntegration;

namespace ExcelRenderer.Drawing;

/// <summary>One source-space decision shared by text measurement, placement and drawing.</summary>
internal readonly record struct ConditionalIconPlacement(bool CanDraw, double Size, double Left, double Right, bool ShowValue)
{
    /// <summary>Preserves one original em for visible values and restores ordinary text when omitted.</summary>
    /// <param name="icon">The immutable icon policy.</param>
    /// <param name="width">Ordinary content width before page scaling.</param>
    /// <param name="fontSize">Original cell font size before shrink-to-fit.</param>
    /// <param name="height">Full source cell height before clipping.</param>
    /// <returns>Usable icon geometry and effective text visibility.</returns>
    internal static ConditionalIconPlacement Resolve(ConditionalIcon? icon, double width, double fontSize, double height)
    {
        if (icon is null)
        {
            return new(false, 0, 0, 0, true);
        }

        var minimumText = icon.ShowValue ? Math.Max(1, fontSize) : 0;
        var size = Math.Min(12, Math.Max(0, width - 2 - minimumText));
        if (size < 3 || height <= 2)
        {
            return new(false, 0, 0, 0, true);
        }

        return new(true, size, icon.RightToLeft ? 0 : size + 2, icon.RightToLeft ? size + 2 : 0, icon.ShowValue);
    }
}
