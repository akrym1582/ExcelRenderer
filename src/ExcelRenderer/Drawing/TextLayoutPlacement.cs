using ExcelRenderer.Abstractions;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>Converts finalized line-relative text coordinates to drawing-command coordinates.</summary>
internal static class TextLayoutPlacement
{
    /// <summary>Places finalized lines without measuring or otherwise changing their contents.</summary>
    /// <param name="layout">The finalized layout. Baselines are relative to each line top.</param>
    /// <param name="bounds">The command bounds in points.</param>
    /// <param name="horizontal">The horizontal alignment.</param>
    /// <param name="vertical">The vertical alignment.</param>
    /// <returns>Lines with absolute left and baseline coordinates.</returns>
    internal static IReadOnlyList<PositionedTextLine> Place(
        TextLayoutResult layout,
        ReportRect bounds,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical)
    {
        var top = vertical switch
        {
            VerticalAlignment.Center => bounds.Y + ((bounds.Height - layout.Size.Height) / 2),
            VerticalAlignment.Bottom => bounds.Y + bounds.Height - layout.Size.Height,
            _ => bounds.Y,
        };
        var result = new PositionedTextLine[layout.Lines.Count];
        for (var index = 0; index < layout.Lines.Count; index++)
        {
            var line = layout.Lines[index];
            var left = horizontal switch
            {
                HorizontalAlignment.Center => bounds.X + ((bounds.Width - line.Width) / 2),
                HorizontalAlignment.Right => bounds.X + bounds.Width - line.Width,
                _ => bounds.X,
            };
            result[index] = new(line, left, top + line.Baseline);
            top += line.Height;
        }

        return result;
    }
}
