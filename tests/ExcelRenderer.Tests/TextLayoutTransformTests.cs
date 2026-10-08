using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>確定済み文字の拡縮、実効フォントサイズの未指定状態、および不正倍率の拒否を検証します。</summary>
public sealed class TextLayoutTransformTests
{
    /// <summary>確定済み文字を拡縮すると、すべての座標・寸法と明示したフォントサイズが倍率に従い、元の状態は変更されないことを検証します。</summary>
    /// <param name="factor">文字の座標・寸法に適用する拡縮倍率。</param>
    [Theory(DisplayName = "確定済み文字を拡縮すると、すべての座標・寸法と明示したフォントサイズが倍率に従い、元の状態は変更されない")]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(1.5)]
    public void Scale_transforms_all_geometry_and_explicit_size(double factor)
    {
        var font = new ResolvedFont("face", 400, false, "face.ttf");
        var run = new TextLayoutRun(new("A", font), 4, 8);
        var line = new TextLayoutLine("A", 20, 12, 9, [run], false)
        {
            Ascent = 7,
            Descent = 2,
            Leading = 1,
        };
        var source = new TextLayoutResult(new(20, 12), [line]) { EffectiveFontSize = 10 };

        var result = TextLayoutTransform.Scale(source, factor);

        Assert.Equal(20 * factor, result.Size.Width);
        Assert.Equal(12 * factor, result.Size.Height);
        Assert.Equal(20 * factor, result.Lines[0].Width);
        Assert.Equal(12 * factor, result.Lines[0].Height);
        Assert.Equal(9 * factor, result.Lines[0].Baseline);
        Assert.Equal(7 * factor, result.Lines[0].Ascent);
        Assert.Equal(2 * factor, result.Lines[0].Descent);
        Assert.Equal(1 * factor, result.Lines[0].Leading);
        Assert.Equal(4 * factor, result.Lines[0].Runs[0].X);
        Assert.Equal(8 * factor, result.Lines[0].Runs[0].Advance);
        Assert.Equal(10 * factor, result.EffectiveFontSize);
        Assert.Equal(20, source.Size.Width);
        Assert.Equal(4, source.Lines[0].Runs[0].X);
    }

    /// <summary>ゼロ倍率でも未指定の実効フォントサイズは未指定のままで、連続する拡縮の結果が倍率の積と一致することを検証します。</summary>
    [Fact(DisplayName = "ゼロ倍率でも未指定の実効フォントサイズは未指定のままで、連続する拡縮の結果が倍率の積と一致する")]
    public void Scale_preserves_unspecified_size_and_composes()
    {
        var source = new TextLayoutResult(new(20, 10), [new("A", 20, 10, 8, [], false)]);

        var zero = TextLayoutTransform.Scale(source, 0);
        var composed = TextLayoutTransform.Scale(TextLayoutTransform.Scale(source, 0.5), 1.5);

        Assert.False(zero.HasExplicitEffectiveFontSize);
        Assert.False(composed.HasExplicitEffectiveFontSize);
        Assert.Equal(15, composed.Size.Width);
        Assert.Equal(6, composed.Lines[0].Baseline);
    }

    /// <summary>負数・NaN・無限大の拡縮倍率を渡すと、共通の文字レイアウト変換で拒否されることを検証します。</summary>
    /// <param name="factor">文字の座標・寸法に適用する拡縮倍率。</param>
    [Theory(DisplayName = "負数・NaN・無限大の拡縮倍率を渡すと、共通の文字レイアウト変換で拒否される")]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Scale_rejects_invalid_factors(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TextLayoutTransform.Scale(new(new(1, 1), []), factor));
    }
}
