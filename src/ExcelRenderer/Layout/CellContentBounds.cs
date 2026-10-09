using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Calculates the single authoritative cell text content rectangle.</summary>
internal static class CellContentBounds
{
    /// <summary>Insets a cell rectangle by padding and alignment-aware indentation.</summary>
    /// <param name="bounds">The complete cell rectangle.</param>
    /// <param name="style">The cell style that supplies alignment and indentation.</param>
    /// <returns>The nonnegative text content rectangle.</returns>
    internal static ReportRect Calculate(ReportRect bounds, CellStyle style)
    {
        var content = Core.Layout.CellContentBounds.Calculate(
            CoreIntegration.CoreCommandAdapter.ToCore(bounds),
            style.Font.Size,
            style.Indent,
            style.HorizontalAlignment == HorizontalAlignment.Right);
        return new(content.X, content.Y, content.Width, content.Height);
    }
}
