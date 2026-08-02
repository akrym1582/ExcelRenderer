using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PngRendererTests
{
    [Fact]
    public void RenderPage_writes_png_at_requested_dpi()
    {
        var commands = new DrawCommand[]
        {
            new FillRectangleCommand(1, new ReportRect(0, 0, 72, 36), new ReportColor(255, 0, 0)),
            new DrawBorderCommand(1, new ReportRect(5, 5, 40, 20), new BorderStyle(new BorderSide(1))),
            new DrawLineCommand(1, 0, 20, 72, 20, new BorderSide(1, new ReportColor(0, 0, 255))),
            new DrawTextCommand(1, new ReportRect(5, 5, 60, 20), "PNG", CellStyle.Default)
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

    [Fact]
    public void Render_writes_one_png_for_each_page()
    {
        var commands = new DrawCommand[]
        {
            new FillRectangleCommand(2, new ReportRect(0, 0, 10, 10), new ReportColor(0, 255, 0)),
            new FillRectangleCommand(1, new ReportRect(0, 0, 10, 10), new ReportColor(255, 0, 0))
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
                CapturedBytes = ToArray();

            base.Dispose(disposing);
        }
    }
}
