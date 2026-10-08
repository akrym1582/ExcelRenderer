using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>PNG と SVG が読み込めない画像をスキップして描画を継続することを検証します。</summary>
public sealed class SkiaImageTests
{
    /// <summary>空、破損、非対応の画像の後も正常な画像と図形が描画されることを検証します。</summary>
    /// <param name="hex">解読不能な画像データの 16 進表記。</param>
    /// <param name="svg">PNG の代わりに SVG 描画を検証するかどうか。</param>
    [Theory(DisplayName = "空、破損、非対応の画像の後も正常な画像と図形が描画される")]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("0102030405", false)]
    [InlineData("0102030405", true)]
    [InlineData("89504E470D0A1A0A", false)]
    [InlineData("89504E470D0A1A0A", true)]
    [InlineData("3C73766720786D6C6E733D22687474703A2F2F7777772E77332E6F72672F323030302F737667222F3E", false)]
    [InlineData("3C73766720786D6C6E733D22687474703A2F2F7777772E77332E6F72672F323030302F737667222F3E", true)]
    public void RenderPage_skips_undecodable_images_and_continues_drawing(string hex, bool svg)
    {
        using var source = new SKBitmap(2, 2);
        source.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(source);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var commands = new DrawCommand[]
        {
            new DrawViewportCommand(
                1,
                [new DrawImageCommand(1, new ReportRect(0, 0, 5, 5), Convert.FromHexString(hex))
                {
                    Rotation = 45,
                    FlipHorizontal = true,
                    ClipBounds = new ReportRect(0, 0, 2, 2),
                }],
                new ReportRect(0, 0, 5, 5),
                5,
                5),
            new DrawImageCommand(1, new ReportRect(10, 0, 10, 10), encoded.ToArray()),
            new FillRectangleCommand(1, new ReportRect(0, 10, 10, 10), new ReportColor(255, 0, 0)),
        };
        using var output = new MemoryStream();
        if (svg)
        {
            new SvgRenderer().RenderPage(commands, new PageSettings(20, 20), output);
            output.Position = 0;
            using var renderedSvg = new Svg.Skia.SKSvg();
            renderedSvg.Load(output);
            Assert.NotNull(renderedSvg.Picture);
            using var bitmap = new SKBitmap(20, 20);
            using var canvas = new SKCanvas(bitmap);
            canvas.DrawPicture(renderedSvg.Picture);
            AssertPixels(bitmap);
        }
        else
        {
            new PngRenderer().RenderPage(commands, new PageSettings(20, 20), output, 72);
            using var bitmap = SKBitmap.Decode(output.ToArray());
            Assert.NotNull(bitmap);
            AssertPixels(bitmap);
        }

        static void AssertPixels(SKBitmap bitmap)
        {
            Assert.Equal(SKColors.White, bitmap.GetPixel(7, 7));
            Assert.Equal(SKColors.Blue, bitmap.GetPixel(15, 5));
            Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 15));
        }
    }
}
