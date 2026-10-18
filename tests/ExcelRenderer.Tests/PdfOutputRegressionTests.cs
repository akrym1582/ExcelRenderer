using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>保存した PDF の内容ストリームから、文字・字形パス・下線の実際の座標を検証します。</summary>
public sealed class PdfOutputRegressionTests
{
    /// <summary>水平・垂直配置を変えた PDF の内容ストリームで、確定済み文字ランの原点と 9 ポイントのフォントサイズを保持することを検証します。</summary>
    /// <param name="horizontal">文字の水平配置。</param>
    /// <param name="vertical">文字の垂直配置。</param>
    /// <param name="ax">文字 A の X 座標の期待値（ポイント）。</param>
    /// <param name="ay">文字 A のベースラインの期待値（ポイント）。</param>
    /// <param name="bx">文字 B の X 座標の期待値（ポイント）。</param>
    /// <param name="by">文字 B のベースラインの期待値（ポイント）。</param>
    /// <param name="cx">文字 C の X 座標の期待値（ポイント）。</param>
    /// <param name="cy">文字 C のベースラインの期待値（ポイント）。</param>
    [Theory(DisplayName = "水平・垂直配置を変えた PDF の内容ストリームで、確定済み文字ランの原点と 9 ポイントのフォントサイズを保持する")]
    [InlineData(HorizontalAlignment.Left, VerticalAlignment.Top, 10, 23, 35, 23, 17, 47)]
    [InlineData(HorizontalAlignment.Center, VerticalAlignment.Center, 40, 33, 65, 33, 57, 57)]
    [InlineData(HorizontalAlignment.Right, VerticalAlignment.Bottom, 70, 43, 95, 43, 97, 67)]
    public void Pdf_finalized_text_uses_stored_run_origins_and_font_size(
        HorizontalAlignment horizontal, VerticalAlignment vertical,
        double ax, double ay, double bx, double by, double cx, double cy)
    {
        var probe = PdfContentProbe.Read(OutputFixture.Pdf(OutputFixture.Artificial(horizontal: horizontal, vertical: vertical)));
        // Each fixture character is a separate run/show, matched in painting order, never by subset character code.
        Assert.Equal(3, probe.Texts.Count);
        Point(probe.Texts[0].Origin, ax, ay);
        Point(probe.Texts[1].Origin, bx, by);
        Point(probe.Texts[2].Origin, cx, cy);
        Assert.All(probe.Texts, t => { Near(9, t.Size); Assert.NotEmpty(t.Encoded); Assert.NotEmpty(t.Font); });
        Assert.Empty(probe.Paints); // Ordinary runs remain text, rather than glyph outlines.
    }

    /// <summary>文字ランの有無と水平配置にかかわらず、PDF の下線が保存済みベースラインに行ごとに一度だけ描画されることを検証します。</summary>
    /// <param name="runs">確定済みレイアウトに文字ランを設定するかどうか。</param>
    /// <param name="underline">文字の下線を有効にするかどうか。</param>
    /// <param name="horizontal">文字の水平配置。</param>
    /// <param name="x1">一行目の下線の始点 X 座標の期待値（ポイント）。</param>
    /// <param name="x2">一行目の下線の終点 X 座標の期待値（ポイント）。</param>
    /// <param name="x3">二行目の下線の始点 X 座標の期待値（ポイント）。</param>
    /// <param name="x4">二行目の下線の終点 X 座標の期待値（ポイント）。</param>
    [Theory(DisplayName = "文字ランの有無と水平配置にかかわらず、PDF の下線が保存済みベースラインに行ごとに一度だけ描画される")]
    [InlineData(true, true, HorizontalAlignment.Left, 10, 50, 10, 30)]
    [InlineData(false, true, HorizontalAlignment.Left, 10, 50, 10, 30)]
    [InlineData(true, false, HorizontalAlignment.Left, 10, 50, 10, 30)]
    [InlineData(false, false, HorizontalAlignment.Left, 10, 50, 10, 30)]
    [InlineData(true, true, HorizontalAlignment.Center, 40, 80, 50, 70)]
    [InlineData(false, true, HorizontalAlignment.Right, 70, 110, 90, 110)]
    public void Pdf_finalized_underline_is_drawn_once_per_line_at_stored_baseline(
        bool runs, bool underline, HorizontalAlignment horizontal, double x1, double x2, double x3, double x4)
    {
        var probe = PdfContentProbe.Read(OutputFixture.Pdf(OutputFixture.Artificial(runs, underline, horizontal)));
        Assert.Equal(2, probe.Texts.Select(text => text.Origin.Y).Distinct().Count());
        Assert.Equal(underline ? 2 : 0, probe.Paints.Count);
        if (!underline) { return; }
        Assert.All(probe.Paints, p => { Assert.Equal("S", p.Operation); Assert.Equal(2, p.Points.Length); });
        Point(probe.Paints[0].Points[0], x1, 24);
        Point(probe.Paints[0].Points[1], x2, 24);
        Point(probe.Paints[1].Points[0], x3, 48);
        Point(probe.Paints[1].Points[1], x4, 48);
    }

    /// <summary>確定済み字形の原点を移動すると、PDF の字形パス全体が同じ差分で移動し、フォントの輪郭寸法にも一致することを検証します。</summary>
    [Fact(DisplayName = "確定済み字形の原点を移動すると、PDF の字形パス全体が同じ差分で移動し、フォントの輪郭寸法にも一致する")]
    public void Pdf_finalized_embedded_glyph_uses_stored_origin()
    {
        using var face = OutputFixture.Typeface();
        using var font = new SKFont(face, 9);
        var glyph = font.GetGlyphs("A")[0];
        using var outline = font.GetGlyphPath(glyph);
        Assert.NotNull(outline);
        Assert.False(outline.IsEmpty);
        DrawTextCommand Command(double x, double baseline) => OutputFixture.Artificial() with
        {
            TextLayout = new(new(40, 30), [new("A", 40, 10, baseline,
                [new(new("A", OutputFixture.Face) { GlyphId = glyph }, x, 5)], false)]) { EffectiveFontSize = 9 },
        };
        var firstBytes = OutputFixture.Pdf(Command(7, 3));
        var shiftedBytes = OutputFixture.Pdf(Command(25, 17));
        var first = PdfContentProbe.Read(firstBytes);
        var shifted = PdfContentProbe.Read(shiftedBytes);
        var a = Assert.Single(first.Texts);
        var b = Assert.Single(shifted.Texts);
        Point(b.Origin, a.Origin.X + 18, a.Origin.Y + 14);
        Point(a.Origin, 17, 23);
        Assert.Equal(9, a.Size);
        using var pdf = TestFixtures.PdfTestFile.Open(new MemoryStream(firstBytes));
        var fonts = pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        var descendant = fonts.Elements.GetDictionary(a.Font)!.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
        var bytes = descendant.Elements.GetDictionary("/FontDescriptor")!.Elements.GetDictionary("/FontFile2")!.Stream.UnfilteredValue;
        using var data = SKData.CreateCopy(bytes);
        using var embeddedFace = SKTypeface.FromData(data);
        using var embeddedFont = new SKFont(embeddedFace, (float)a.Size);
        Assert.Equal(2, a.Encoded.Length);
        var embeddedGlyph = (ushort)((a.Encoded[0] << 8) | a.Encoded[1]);
        using var embeddedPath = embeddedFont.GetGlyphPath(embeddedGlyph);
        Assert.NotNull(embeddedPath);
        Assert.Equal(outline.ToSvgPathData(), embeddedPath.ToSvgPathData());
        Near(17 + outline.Bounds.Left, a.Origin.X + embeddedPath.Bounds.Left);
        Near(23 + outline.Bounds.Top, a.Origin.Y + embeddedPath.Bounds.Top);
    }

    /// <summary>点の X・Y 座標が、それぞれ指定した期待値と許容誤差内で一致することを検証します。</summary>
    /// <param name="actual">比較対象の実際値。</param>
    /// <param name="x">描画位置または比較対象の X 座標。</param>
    /// <param name="y">描画位置または比較対象の Y 座標。</param>
    internal static void Point(PdfContentProbe.Point actual, double x, double y) { Near(x, actual.X); Near(y, actual.Y); }

    /// <summary>座標の実際値が期待値と許容誤差内で一致することを検証します。</summary>
    /// <param name="expected">判定または数値比較の期待値。</param>
    /// <param name="actual">比較対象の実際値。</param>
    internal static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 0.05, $"Expected {expected}, got {actual}");
}
