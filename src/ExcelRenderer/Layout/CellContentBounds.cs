using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Calculates the single authoritative cell text content rectangle.</summary>
internal static class CellContentBounds
{
    private const double Padding = 0.5;

    /// <summary>Insets a cell rectangle by padding and alignment-aware indentation.</summary>
    /// <param name="bounds">The complete cell rectangle.</param>
    /// <param name="style">The cell style that supplies alignment and indentation.</param>
    /// <returns>The nonnegative text content rectangle.</returns>
    internal static ReportRect Calculate(ReportRect bounds, CellStyle style)
    {
        var horizontalPadding = Math.Min(Padding, bounds.Width / 2);
        var verticalPadding = Math.Min(Padding, bounds.Height / 2);
        var indent = Math.Min(GetIndentWidth(style), Math.Max(0, bounds.Width - (horizontalPadding * 2)));
        var leftIndent = style.HorizontalAlignment == HorizontalAlignment.Right ? 0 : indent;
        var rightIndent = style.HorizontalAlignment == HorizontalAlignment.Right ? indent : 0;
        return new(
            bounds.X + horizontalPadding + leftIndent,
            bounds.Y + verticalPadding,
            Math.Max(0, bounds.Width - (horizontalPadding * 2) - leftIndent - rightIndent),
            Math.Max(0, bounds.Height - (verticalPadding * 2)));
    }

    private static double GetIndentWidth(CellStyle style) => Math.Max(0, style.Indent) * style.Font.Size * 0.5;
}
