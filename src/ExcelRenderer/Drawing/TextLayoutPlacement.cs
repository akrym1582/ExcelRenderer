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
    => Core.Drawing.TextLayoutPlacement.Place(
        layout.Lines,
        layout.Size.Height,
        CoreIntegration.CoreCommandAdapter.ToCore(bounds),
        CoreIntegration.CoreModelAdapter.ToCore(horizontal),
        CoreIntegration.CoreModelAdapter.ToCore(vertical),
        line => (line.Width, line.Height, line.Baseline),
        (line, x, baseline) => new PositionedTextLine(line, x, baseline));
}
