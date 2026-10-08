using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>確定済み文字の行相対ベースラインと文字ランの横オフセットによる配置を検証します。</summary>
public sealed class TextLayoutPlacementTests
{
    /// <summary>水平・垂直配置を変更しても、行相対のベースラインと文字ランの横オフセットが独立して描画位置に反映されることを検証します。</summary>
    /// <param name="horizontal">文字の水平配置。</param>
    /// <param name="expectedLeft">先頭行の左端座標の期待値（ポイント）。</param>
    /// <param name="vertical">文字の垂直配置。</param>
    /// <param name="expectedBaseline">先頭行のベースライン座標の期待値（ポイント）。</param>
    [Theory(DisplayName = "水平・垂直配置を変更しても、行相対のベースラインと文字ランの横オフセットが独立して描画位置に反映される")]
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
