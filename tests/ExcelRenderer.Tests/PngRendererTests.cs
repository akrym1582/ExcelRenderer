using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// 描画命令から PNG ページを生成するレンダラーの出力を検証します。
/// </summary>
public sealed class PngRendererTests
{
    /// <summary>二重線の 2 本の線と間の空白が PNG 上に残ることを検証します。</summary>
    [Fact]
    public void RenderPage_draws_two_separate_lines_for_double_border()
    {
        using var output = new MemoryStream();
        new PngRenderer().RenderPage(
            [new DrawBorderCommand(
                1,
                new ReportRect(5, 10, 60, 20),
                new BorderStyle(Top: new BorderSide(1, LineStyle: BorderLineStyle.Double)))],
            new PageSettings(72, 40),
            output,
            144);

        using var bitmap = SKBitmap.Decode(output.ToArray());
        Assert.True(bitmap.GetPixel(40, 21).Red < 100);
        Assert.Equal(SKColors.White, bitmap.GetPixel(40, 24));
        Assert.True(bitmap.GetPixel(40, 27).Red < 100);
    }

    /// <summary>
    /// 点線罫線を描画したとき、線分間の空白が塗りつぶされずに保持されることを検証します。
    /// </summary>
    [Fact]
    public void RenderPage_preserves_gaps_in_dotted_borders()
    {
        using var output = new MemoryStream();
        new PngRenderer().RenderPage(
            [new DrawBorderCommand(
                1,
                new ReportRect(4, 10, 60, 10),
                new BorderStyle(Top: new BorderSide(2, LineStyle: BorderLineStyle.Dotted)))],
            new PageSettings(72, 24),
            output,
            72);

        using var bitmap = SKBitmap.Decode(output.ToArray());
        for (var x = 4; x < 60; x += 6)
        {
            Assert.True(bitmap.GetPixel(x, 10).Red < 50, $"Missing dot at {x}");
            Assert.Equal(SKColors.White, bitmap.GetPixel(x + 3, 10));
        }
    }

    /// <summary>
    /// 指定した DPI が PNG の解像度メタデータとピクセル寸法に反映されることを検証します。
    /// </summary>
    [Fact]
    public void RenderPage_writes_png_at_requested_dpi()
    {
        var commands = new DrawCommand[]
        {
            new FillRectangleCommand(1, new ReportRect(0, 0, 72, 36), new ReportColor(255, 0, 0)),
            new DrawBorderCommand(1, new ReportRect(5, 5, 40, 20), new BorderStyle(new BorderSide(1))),
            new DrawLineCommand(1, 0, 20, 72, 20, new BorderSide(1, new ReportColor(0, 0, 255))),
            new DrawTextCommand(1, new ReportRect(5, 5, 60, 20), "PNG", CellStyle.Default),
        };
        using var output = new MemoryStream();

        new PngRenderer().RenderPage(commands, new PageSettings(72, 36), output, 144);

        var bytes = output.ToArray();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(144, bitmap.Width);
        Assert.Equal(72, bitmap.Height);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(100, 60));
    }

    /// <summary>
    /// 複数ページの描画結果がページごとに個別の PNG ファイルへ出力されることを検証します。
    /// </summary>
    [Fact]
    public void Render_writes_one_png_for_each_page()
    {
        var commands = new DrawCommand[]
        {
            new FillRectangleCommand(2, new ReportRect(0, 0, 10, 10), new ReportColor(0, 255, 0)),
            new FillRectangleCommand(1, new ReportRect(0, 0, 10, 10), new ReportColor(255, 0, 0)),
        };
        var outputs = new Dictionary<int, MemoryStream>();

        new PngRenderer().Render(commands, new PageSettings(20, 20), pageNumber =>
        {
            var stream = new MemoryStream();
            outputs.Add(pageNumber, stream);
            return stream;
        });

        Assert.Equal([1, 2], outputs.Keys.OrderBy(pageNumber => pageNumber).ToArray());
        Assert.All(outputs.Values, output =>
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, output.ToArray()[..4]));
    }

    /// <summary>
    /// ページ内の埋め込み画像が指定位置へ描画されることを検証します。
    /// </summary>
    [Fact]
    public void RenderPage_renders_an_embedded_image()
    {
        using var sourceBitmap = new SKBitmap(2, 2);
        sourceBitmap.Erase(SKColors.Blue);
        using var sourceImage = SKImage.FromBitmap(sourceBitmap);
        using var encoded = sourceImage.Encode(SKEncodedImageFormat.Png, 100);
        var command = new DrawImageCommand(1, new ReportRect(0, 0, 10, 10), encoded.ToArray());
        using var output = new MemoryStream();

        new PngRenderer().RenderPage([command], new PageSettings(10, 10), output, 72);

        using var rendered = SKBitmap.Decode(output.ToArray());
        Assert.NotNull(rendered);
        Assert.Equal(SKColors.Blue, rendered.GetPixel(5, 5));
    }

    /// <summary>
    /// 描画命令がない文書でも空の先頭ページが PNG として出力されることを検証します。
    /// </summary>
    [Fact]
    public void Render_writes_a_blank_first_page_when_there_are_no_commands()
    {
        var pageNumber = 0;
        var output = new CaptureOnDisposeStream();

        new PngRenderer().Render([], new PageSettings(10, 10), number =>
        {
            pageNumber = number;
            return output;
        });

        Assert.Equal(1, pageNumber);
        Assert.NotEmpty(output.CapturedBytes);
    }

    /// <summary>
    /// ゼロ以下の DPI を指定した場合に引数エラーとなることを検証します。
    /// </summary>
    [Fact]
    public void RenderPage_rejects_non_positive_dpi()
    {
        using var output = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PngRenderer().RenderPage([], new PageSettings(), output, 0));
    }

    private sealed class CaptureOnDisposeStream : MemoryStream
    {
        public byte[] CapturedBytes { get; private set; } = [];

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CapturedBytes = ToArray();
            }

            base.Dispose(disposing);
        }
    }
}
