using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Skia の従来文字と文字ランのない確定済み文字が、選択済み書体で計測・描画されることを検証します。</summary>
public sealed class SkiaFontRegressionTests
{
    /// <summary>従来形式の Skia 文字を計測して描画する際に、選択済みの同じ書体を使うことを検証します。</summary>
    /// <param name="bold">太字書体を要求するかどうか。</param>
    /// <param name="italic">斜体書体を要求するかどうか。</param>
    /// <param name="shrink">文字を領域内に収まるよう縮小するかどうか。</param>
    /// <param name="paths">参照文字を字形パスとして描画するかどうか。</param>
    /// <param name="wrap">文字を利用可能な幅で折り返すかどうか。</param>
    /// <param name="alignment">検証する文字の水平配置。</param>
    [Theory(DisplayName = "従来形式の Skia 文字を計測して描画する際に、選択済みの同じ書体を使う")]
    [InlineData(false, false, false, false, false, HorizontalAlignment.Left)]
    [InlineData(false, false, false, true, false, HorizontalAlignment.Left)]
    [InlineData(true, false, false, false, false, HorizontalAlignment.Left)]
    [InlineData(false, true, false, true, false, HorizontalAlignment.Left)]
    [InlineData(false, false, true, false, false, HorizontalAlignment.Center)]
    [InlineData(false, false, true, true, false, HorizontalAlignment.Center)]
    [InlineData(false, false, false, true, false, HorizontalAlignment.Right)]
    [InlineData(false, false, false, false, true, HorizontalAlignment.Left)]
    public void Skia_legacy_uses_selected_face_for_measurement_and_drawing(
        bool bold, bool italic, bool shrink, bool paths, bool wrap, HorizontalAlignment alignment)
    {
        var text = wrap ? "iiiiWWWW\nWWii" : "iiiiWWWW";
        var command = new DrawTextCommand(1, new(10, 10, shrink || wrap ? 35 : 100, 65), text,
            CellStyle.Default with
            {
                Font = new("Controlled Regular", 18, Bold: bold, Italic: italic),
                HorizontalAlignment = alignment,
                ShrinkToFit = shrink,
                WrapText = wrap,
            });
        using var selected = OutputFixture.Typeface();
        using var different = OutputFixture.Typeface(mono: true);
        using var selectedFont = new SKFont(selected, 18);
        using var differentFont = new SKFont(different, 18);
        Assert.True(Math.Abs(selectedFont.MeasureText("iiiiWWWW") - differentFont.MeasureText("iiiiWWWW")) > 1);
        using var expected = Reference(command, selected, paths);
        using var wrong = Reference(command, different, paths);
        Assert.False(expected.Bytes.SequenceEqual(wrong.Bytes));
        using var actual = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(actual);
        canvas.Clear(SKColors.White);
        var calls = 0;
        var drawing = new SkiaTextDrawing(paths, null, requested =>
        {
            calls++;
            Assert.Equal(bold, requested.Bold);
            Assert.Equal(italic, requested.Italic);
            return OutputFixture.Typeface();
        });
        drawing.PaintLegacy(canvas, command);
        Assert.Equal(1, calls);
        Assert.Equal(expected.Bytes, actual.Bytes);
    }

    /// <summary>確定済みレイアウトに文字ランがない場合、Skia が選択済みの主書体を使って描画することを検証します。</summary>
    /// <param name="paths">参照文字を字形パスとして描画するかどうか。</param>
    [Theory(DisplayName = "確定済みレイアウトに文字ランがない場合、Skia が選択済みの主書体を使って描画する")]
    [InlineData(false)]
    [InlineData(true)]
    public void Skia_finalized_empty_runs_use_selected_primary_face(bool paths)
    {
        var command = OutputFixture.Artificial(runs: false);
        using var expected = new SKBitmap(120, 80);
        using var expectedCanvas = new SKCanvas(expected);
        expectedCanvas.Clear(SKColors.White);
        using var face = OutputFixture.Typeface();
        using var font = new SKFont(face, 9);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        Draw(expectedCanvas, "AB", 10, 23, font, paint, paths);
        Draw(expectedCanvas, "C", 10, 47, font, paint, paths);
        using var actual = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(actual);
        canvas.Clear(SKColors.White);
        var calls = 0;
        new SkiaTextDrawing(paths, null, _ => { calls++; return OutputFixture.Typeface(); })
            .PaintFinalized(canvas, command, command.TextLayout!);
        Assert.Equal(1, calls);
        Assert.Equal(expected.Bytes, actual.Bytes);
    }

    /// <summary>指定した書体で文字を参照描画し、対象の Skia 描画結果と比較するビットマップを作成します。</summary>
    /// <param name="command">実行するコマンド、または描画対象の文字命令。</param>
    /// <param name="face">文字を参照描画するための選択済み書体。</param>
    /// <param name="paths">参照文字を字形パスとして描画するかどうか。</param>
    private static SKBitmap Reference(DrawTextCommand command, SKTypeface face, bool paths)
    {
        var bitmap = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var font = new SKFont(face, 18);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        var paragraphs = command.Text.Split('\n');
        if (command.Style.ShrinkToFit)
        {
            var widest = paragraphs.Max(p => font.MeasureText(p, paint));
            font.Size *= (float)(command.Bounds.Width / widest);
        }

        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var line = string.Empty;
            foreach (var character in paragraph)
            {
                if (command.Style.WrapText && line.Length > 0 && font.MeasureText(line + character, paint) > command.Bounds.Width)
                {
                    lines.Add(line);
                    line = string.Empty;
                }
                line += character;
            }
            lines.Add(line);
        }
        if (command.Style.WrapText || command.Style.ShrinkToFit)
        {
            canvas.ClipRect(new(10, 10, (float)(10 + command.Bounds.Width), 75));
        }
        var y = 10 - font.Metrics.Ascent;
        foreach (var line in lines)
        {
            var width = font.MeasureText(line, paint);
            var x = command.Style.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => 10 + ((float)command.Bounds.Width - width) / 2,
                HorizontalAlignment.Right => 10 + (float)command.Bounds.Width - width,
                _ => 10,
            };
            Draw(canvas, line, x, y, font, paint, paths);
            y += font.Metrics.Descent - font.Metrics.Ascent + font.Metrics.Leading;
        }
        return bitmap;
    }

    /// <summary>指定位置に通常の文字または字形パスを描き、参照画素を作成します。</summary>
    /// <param name="canvas">文字の参照描画先となるキャンバス。</param>
    /// <param name="text">計測・描画または解析の対象となる文字列。</param>
    /// <param name="x">描画位置または比較対象の X 座標。</param>
    /// <param name="y">描画位置または比較対象の Y 座標。</param>
    /// <param name="font">文字計測または参照描画に使うフォント設定。</param>
    /// <param name="paint">参照描画の色とアンチエイリアス設定。</param>
    /// <param name="paths">参照文字を字形パスとして描画するかどうか。</param>
    private static void Draw(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint, bool paths)
    {
        if (paths)
        {
            using var path = font.GetTextPath(text, new SKPoint(x, y));
            canvas.DrawPath(path, paint);
        }
        else
        {
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        }
    }
}
