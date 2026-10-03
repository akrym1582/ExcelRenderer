using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Verifies the renderer-independent finalized text placement contract.</summary>
public sealed class TextLayoutPlacementTests
{
    /// <summary>Line baselines and run offsets remain independent under every alignment.</summary>
    /// <param name="horizontal">Horizontal alignment.</param>
    /// <param name="expectedLeft">Expected first-line origin.</param>
    /// <param name="vertical">Vertical alignment.</param>
    /// <param name="expectedBaseline">Expected first baseline.</param>
    [Theory]
    [InlineData(HorizontalAlignment.Left, 10, VerticalAlignment.Top, 23)]
    [InlineData(HorizontalAlignment.Center, 40, VerticalAlignment.Center, 58)]
    [InlineData(HorizontalAlignment.Right, 70, VerticalAlignment.Bottom, 93)]
    public void Finalized_lines_use_line_relative_baselines_and_alignment(
        HorizontalAlignment horizontal,
        double expectedLeft,
        VerticalAlignment vertical,
        double expectedBaseline)
    {
        var layout = new TextLayoutResult(
            new(40, 30),
            [
                new("first", 40, 10, 3, [], false),
                new("second", 20, 20, 17, [], false),
            ])
        {
            EffectiveFontSize = 9,
        };

        var placed = TextLayoutPlacement.Place(layout, new(10, 20, 100, 100), horizontal, vertical);

        Assert.Equal(expectedLeft, placed[0].Left, 6);
        Assert.Equal(expectedBaseline, placed[0].Baseline, 6);
        Assert.Equal(placed[0].Baseline + 24, placed[1].Baseline, 6);
    }
}
