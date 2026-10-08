using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>保存した SVG を再読込し、文字・回転・下線の描画位置を PNG と比較します。</summary>
public sealed class SerializedSvgRegressionTests
{
    /// <summary>配置を変えた確定済み文字の SVG を再読込し、字形パスの座標とラスタ化した描画範囲が PNG に一致することを検証します。</summary>
    /// <param name="horizontal">文字の水平配置。</param>
    /// <param name="vertical">文字の垂直配置。</param>
    /// <param name="ax">文字 A の X 座標の期待値（ポイント）。</param>
    /// <param name="ay">文字 A のベースラインの期待値（ポイント）。</param>
    /// <param name="bx">文字 B の X 座標の期待値（ポイント）。</param>
    /// <param name="by">文字 B のベースラインの期待値（ポイント）。</param>
    /// <param name="cx">文字 C の X 座標の期待値（ポイント）。</param>
    /// <param name="cy">文字 C のベースラインの期待値（ポイント）。</param>
    [Theory(DisplayName = "配置を変えた確定済み文字の SVG を再読込し、字形パスの座標とラスタ化した描画範囲が PNG に一致する")]
    [InlineData(HorizontalAlignment.Left, VerticalAlignment.Top, 10, 23, 35, 23, 17, 47)]
    [InlineData(HorizontalAlignment.Center, VerticalAlignment.Center, 40, 33, 65, 33, 57, 57)]
    [InlineData(HorizontalAlignment.Right, VerticalAlignment.Bottom, 70, 43, 95, 43, 97, 67)]
    public void Svg_serialized_finalized_text_matches_png_geometry(HorizontalAlignment horizontal, VerticalAlignment vertical,
        float ax, float ay, float bx, float by, float cx, float cy)
    {
        using var probe = new SerializedSvgProbe(OutputFixture.Artificial(horizontal: horizontal, vertical: vertical));
        probe.Compare($"finalized-{horizontal}", () =>
        {
            using var face = OutputFixture.Typeface();
            using var font = new SKFont(face, 9);
            CheckGlyph(probe, font, "A", ax, ay);
            CheckGlyph(probe, font, "B", bx, by);
            CheckGlyph(probe, font, "C", cx, cy);
            // Independently inspect the serialized path coordinates to a stricter 0.05pt tolerance.
            XNamespace svg = "http://www.w3.org/2000/svg";
            var paths = XDocument.Parse(Encoding.UTF8.GetString(probe.Svg)).Descendants(svg + "path").ToArray();
            Assert.Equal(3, paths.Length);
            var origins = new[] { ("A", ax, ay), ("B", bx, by), ("C", cx, cy) };
            for (var i = 0; i < paths.Length; i++)
            {
                Assert.Null(paths[i].Attribute("transform"));
                using var serialized = SKPath.ParseSvgPathData(paths[i].Attribute("d")!.Value);
                using var expected = font.GetTextPath(origins[i].Item1, new SKPoint(origins[i].Item2, origins[i].Item3));
                Assert.NotNull(serialized);
                var a = serialized.Bounds; var b = expected.Bounds;
                PdfOutputRegressionTests.Near(b.Left, a.Left); PdfOutputRegressionTests.Near(b.Top, a.Top);
                PdfOutputRegressionTests.Near(b.Right, a.Right); PdfOutputRegressionTests.Near(b.Bottom, a.Bottom);
            }
        });
    }

    /// <summary>従来形式の折り返し・縮小・水平配置を変えた SVG を再読込し、各行の描画範囲が PNG に一致することを検証します。</summary>
    /// <param name="wrap">文字を利用可能な幅で折り返すかどうか。</param>
    /// <param name="shrink">文字を領域内に収まるよう縮小するかどうか。</param>
    /// <param name="alignment">検証する文字の水平配置。</param>
    [Theory(DisplayName = "従来形式の折り返し・縮小・水平配置を変えた SVG を再読込し、各行の描画範囲が PNG に一致する")]
    [InlineData(true, false, HorizontalAlignment.Left)]
    [InlineData(false, true, HorizontalAlignment.Center)]
    [InlineData(false, true, HorizontalAlignment.Right)]
    public void Svg_serialized_legacy_text_matches_png_geometry(bool wrap, bool shrink, HorizontalAlignment alignment)
    {
        var command = new DrawTextCommand(1, new(10, 10, 45, 65), "iiiiWWWW\nWWii", CellStyle.Default with
        {
            Font = new("Noto Sans JP", 18), WrapText = wrap, ShrinkToFit = shrink, HorizontalAlignment = alignment,
        });
        using var probe = new SerializedSvgProbe(command);
        probe.Compare($"legacy-{wrap}-{alignment}", () =>
        {
            var first = new SKRectI(10, 10, 55, wrap ? 35 : 25);
            var second = new SKRectI(10, wrap ? 35 : 25, 55, wrap ? 65 : 50);
            BoundsMatch(probe, first);
            BoundsMatch(probe, second);
            if (wrap) { BoundsMatch(probe, new(10, 65, 55, 75)); }
        });
    }

    /// <summary>30 度回転した文字の SVG を再読込しても、PNG と同じクリップ内に描画され、領域外に文字画素が出ないことを検証します。</summary>
    [Fact(DisplayName = "30 度回転した文字の SVG を再読込しても、PNG と同じクリップ内に描画され、領域外に文字画素が出ない")]
    public void Svg_serialized_rotation_preserves_clip()
    {
        var command = new DrawTextCommand(1, new(25, 20, 55, 30), "WWWWWWWW\nWWWWWWWW", CellStyle.Default with
        {
            Font = new("Noto Sans JP", 18), WrapText = true, TextRotation = 30,
        });
        using var probe = new SerializedSvgProbe(command);
        probe.Compare("rotation-clip", () =>
        {
            BoundsMatch(probe, new(25, 20, 80, 50));
            foreach (var bitmap in new[] { probe.Png, probe.Raster })
            for (var y = 0; y < 80; y++)
            for (var x = 0; x < 120; x++)
            {
                if (x < 25 || x >= 80 || y < 20 || y >= 50) { Assert.False(SerializedSvgProbe.Ink(bitmap, x, y)); }
            }
        });
    }

    /// <summary>確定済み文字の SVG に二行分の下線が保存済み位置で一度ずつ出力され、再描画した画素も PNG に一致することを検証します。</summary>
    [Fact(DisplayName = "確定済み文字の SVG に二行分の下線が保存済み位置で一度ずつ出力され、再描画した画素も PNG に一致する")]
    public void Svg_serialized_finalized_underline_has_stored_position_and_count()
    {
        using var probe = new SerializedSvgProbe(OutputFixture.Artificial(underline: true));
        probe.Compare("underline", () =>
        {
            using var face = OutputFixture.Typeface(); using var font = new SKFont(face, 9);
            CheckGlyph(probe, font, "A", 10, 23); CheckGlyph(probe, font, "B", 35, 23); CheckGlyph(probe, font, "C", 17, 47);
            var document = XDocument.Parse(Encoding.UTF8.GetString(probe.Svg));
            XNamespace svg = "http://www.w3.org/2000/svg";
            var strokes = document.Descendants(svg + "path").Where(p => p.Attribute("stroke") is not null).ToArray();
            Assert.Equal(2, strokes.Length);
            var expected = new[] { new[] { 10d, 24, 50, 24 }, new[] { 10d, 48, 30, 48 } };
            for (var i = 0; i < 2; i++)
            {
                var numbers = Regex.Matches(strokes[i].Attribute("d")!.Value, @"-?\d+(?:\.\d+)?")
                    .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                Assert.Equal(4, numbers.Length);
                for (var j = 0; j < 4; j++) { PdfOutputRegressionTests.Near(expected[i][j], numbers[j]); }
            }
            foreach (var bitmap in new[] { probe.Png, probe.Raster })
            {
                for (var x = 11; x < 49; x++) { Assert.True(SerializedSvgProbe.Ink(bitmap, x, 24) || SerializedSvgProbe.Ink(bitmap, x, 23)); }
                for (var x = 11; x < 29; x++) { Assert.True(SerializedSvgProbe.Ink(bitmap, x, 48) || SerializedSvgProbe.Ink(bitmap, x, 47)); }
                Assert.False(SerializedSvgProbe.Ink(bitmap, 55, 24));
                Assert.False(SerializedSvgProbe.Ink(bitmap, 35, 48));
            }
        });
    }

    /// <summary>期待する字形を独立して描き、下線を除いた領域の文字画素と SVG・PNG の描画範囲を比較します。</summary>
    /// <param name="probe">PNG と SVG の出力比較に使用する検証器。</param>
    /// <param name="font">文字計測または参照描画に使うフォント設定。</param>
    /// <param name="text">計測・描画または解析の対象となる文字列。</param>
    /// <param name="x">描画位置または比較対象の X 座標。</param>
    /// <param name="baseline">文字を描画するベースラインの Y 座標。</param>
    private static void CheckGlyph(SerializedSvgProbe probe, SKFont font, string text, float x, float baseline)
    {
        using var path = font.GetTextPath(text, new SKPoint(x, baseline));
        var b = path.Bounds;
        // Inspection regions stop above the underline. A background or underline alone cannot satisfy these.
        var region = new SKRectI((int)Math.Floor(b.Left) - 1, (int)Math.Floor(b.Top) - 1,
            (int)Math.Ceiling(b.Right) + 1, (int)Math.Ceiling(b.Bottom));
        using var reference = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(reference);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.Clear(SKColors.White);
        canvas.DrawPath(path, paint);
        var expected = SerializedSvgProbe.InkBounds(reference, region);
        foreach (var bitmap in new[] { probe.Png, probe.Raster })
        {
            var ink = SerializedSvgProbe.InkBounds(bitmap, region);
            Assert.InRange(Math.Abs(ink.Left - expected.Left), 0, 1);
            Assert.InRange(Math.Abs(ink.Top - expected.Top), 0, 1);
            Assert.InRange(Math.Abs(ink.Right - expected.Right), 0, 1);
            Assert.InRange(Math.Abs(ink.Bottom - expected.Bottom), 0, 1);
        }
        BoundsMatch(probe, region);
    }

    /// <summary>指定領域内の PNG と SVG 再描画の外接矩形を、各辺 1 画素以内の差で比較します。</summary>
    /// <param name="probe">PNG と SVG の出力比較に使用する検証器。</param>
    /// <param name="region">文字画素を検査する矩形領域。</param>
    private static void BoundsMatch(SerializedSvgProbe probe, SKRectI region)
    {
        var a = SerializedSvgProbe.InkBounds(probe.Png, region); var b = SerializedSvgProbe.InkBounds(probe.Raster, region);
        Assert.InRange(Math.Abs(a.Left - b.Left), 0, 1); Assert.InRange(Math.Abs(a.Top - b.Top), 0, 1);
        Assert.InRange(Math.Abs(a.Right - b.Right), 0, 1); Assert.InRange(Math.Abs(a.Bottom - b.Bottom), 0, 1);
    }
}
