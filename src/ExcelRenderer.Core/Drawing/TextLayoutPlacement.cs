using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Drawing;

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
    => Place(
        layout.Lines,
        layout.Size.Height,
        bounds,
        horizontal,
        vertical,
        line => (line.Width, line.Height, line.Baseline),
        (line, x, baseline) => new PositionedTextLine(line, x, baseline));

    /// <summary>Places finalized line objects using their already measured geometry.</summary>
    /// <typeparam name="TLine">The borrowed line type.</typeparam>
    /// <typeparam name="TResult">The placed line type.</typeparam>
    /// <param name="lines">The finalized lines.</param>
    /// <param name="totalHeight">The finalized total height.</param>
    /// <param name="bounds">The content rectangle.</param>
    /// <param name="horizontal">The horizontal alignment.</param>
    /// <param name="vertical">The vertical alignment.</param>
    /// <param name="metrics">The finalized line metrics.</param>
    /// <param name="place">The placed-result constructor.</param>
    /// <returns>The placed lines, retaining their original payloads.</returns>
    internal static IReadOnlyList<TResult> Place<TLine, TResult>(
        IReadOnlyList<TLine> lines,
        double totalHeight,
        ReportRect bounds,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        Func<TLine, (double Width, double Height, double Baseline)> metrics,
        Func<TLine, double, double, TResult> place)
    {
        var top = vertical switch
        {
            VerticalAlignment.Center => bounds.Y + ((bounds.Height - totalHeight) / 2),
            VerticalAlignment.Bottom => bounds.Y + bounds.Height - totalHeight,
            _ => bounds.Y,
        };
        var result = new TResult[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var measured = metrics(line);
            var left = horizontal switch
            {
                HorizontalAlignment.Center => bounds.X + ((bounds.Width - measured.Width) / 2),
                HorizontalAlignment.Right => bounds.X + bounds.Width - measured.Width,
                _ => bounds.X,
            };
            result[index] = place(line, left, top + measured.Baseline);
            top += measured.Height;
        }

        return result;
    }
}
