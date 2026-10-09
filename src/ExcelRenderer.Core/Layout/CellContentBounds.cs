using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Calculates the single authoritative cell text content rectangle.</summary>
internal static class CellContentBounds
{
    private const double Padding = 0.5;

    /// <summary>Insets a cell rectangle by padding and alignment-aware indentation.</summary>
    /// <param name="bounds">The complete cell rectangle.</param>
    /// <param name="style">The cell style that supplies alignment and indentation.</param>
    /// <returns>The nonnegative text content rectangle.</returns>
    internal static ReportRect Calculate(ReportRect bounds, CellStyle style)
    => Calculate(bounds, style.Font.Size, style.Indent, style.HorizontalAlignment == HorizontalAlignment.Right);

    /// <summary>Insets content using neutral font and alignment metadata.</summary>
    /// <param name="bounds">The cell bounds.</param>
    /// <param name="fontSize">The font size.</param>
    /// <param name="indentLevel">The indentation level.</param>
    /// <param name="rightAligned">Whether indentation belongs on the right.</param>
    /// <returns>The text content bounds.</returns>
    internal static ReportRect Calculate(ReportRect bounds, double fontSize, int indentLevel, bool rightAligned)
    {
        var horizontalPadding = Math.Min(Padding, bounds.Width / 2);
        var verticalPadding = Math.Min(Padding, bounds.Height / 2);
        var indent = Math.Min(Math.Max(0, indentLevel) * fontSize * 0.5, Math.Max(0, bounds.Width - (horizontalPadding * 2)));
        var leftIndent = rightAligned ? 0 : indent;
        var rightIndent = rightAligned ? indent : 0;
        return new(
            bounds.X + horizontalPadding + leftIndent,
            bounds.Y + verticalPadding,
            Math.Max(0, bounds.Width - (horizontalPadding * 2) - leftIndent - rightIndent),
            Math.Max(0, bounds.Height - (verticalPadding * 2)));
    }
}
