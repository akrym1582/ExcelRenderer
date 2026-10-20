using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class PdfDrawingTests
{
    [Fact]
    public void Saved_pdf_preserves_fill_line_and_image_geometry_and_colors()
    {
        DrawCommand[] commands =
        [
            new FillRectangleCommand(1, new(20, 30, 40, 25), new(51, 102, 153)),
            new DrawLineCommand(1, 70, 80, 110, 80, new(2, new(204, 51, 0), BorderLineStyle.Solid)),
            new DrawImageCommand(1, new(120, 40, 30, 20), TestSupport.Png(SKColors.Lime)),
        ];
        using var renderer = new CorePdfRenderer();
        renderer.Prepare();
        renderer.AppendPage(new(200, 160), commands);
        using var output = new MemoryStream();
        renderer.Complete(output);
        using var pdf = TestSupport.Open(output.ToArray());
        var page = pdf.GetPage(1);
        TestSupport.Near(200, page.Width);
        TestSupport.Near(160, page.Height);
        var fill = Assert.Single(page.Paths, path => path.IsFilled);
        Assert.Equal((0.2, 0.4, 0.6), fill.FillColor!.ToRGBValues());
        var rectangle = fill.Single().GetBoundingRectangle()!.Value;
        Assert.Equal(new PdfRectangle(20, 105, 60, 130), rectangle);
        var stroke = Assert.Single(page.Paths, path => path.IsStroked);
        Assert.Equal((0.8, 0.2, 0d), stroke.StrokeColor!.ToRGBValues());
        TestSupport.Near(2, stroke.LineWidth);
        var line = Assert.Single(stroke.Single().Commands.OfType<PdfSubpath.Line>());
        Assert.Equal(new PdfPoint(70, 80), line.From);
        Assert.Equal(new PdfPoint(110, 80), line.To);
        var image = Assert.Single(page.GetImages());
        Assert.Equal(new PdfRectangle(120, 100, 150, 120), image.BoundingBox);
        Assert.True(image.TryGetPng(out var png));
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(3, bitmap.Width);
        Assert.Equal(3, bitmap.Height);
        Assert.All(bitmap.Pixels, color => Assert.Equal(SKColors.Lime, color));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(1)]
    [InlineData(5)]
    public void Saved_border_has_expected_strokes_and_visible_dash_segments(int styleValue)
    {
        var style = (BorderLineStyle)styleValue;
        var side = new BorderSide(1, new(0, 0, 0), style);
        using var renderer = new CorePdfRenderer();
        renderer.Prepare();
        renderer.AppendPage(new(200, 160), [new DrawBorderCommand(1, new(20, 30, 40, 25), new(side, side, side, side))]);
        using var output = new MemoryStream();
        renderer.Complete(output);
        using var pdf = TestSupport.Open(output.ToArray());
        if (style is BorderLineStyle.Dashed or BorderLineStyle.Dotted)
        {
            // Skia emits dash effects as filled segment outlines in PDF.
            var segments = pdf.GetPage(1).Paths.Where(path => path.IsFilled)
                .SelectMany(path => path).Select(path => path.GetBoundingRectangle()!.Value).ToArray();
            Assert.Equal(style == BorderLineStyle.Dashed ? 26 : 46, segments.Length);
            Assert.All(segments, segment =>
            {
                TestSupport.Near(1, Math.Min(segment.Width, segment.Height));
                TestSupport.Near(style == BorderLineStyle.Dashed ? 3 : 1, Math.Max(segment.Width, segment.Height));
            });
            return;
        }

        var strokes = pdf.GetPage(1).Paths.Where(path => path.IsStroked).ToArray();
        Assert.Equal(style == BorderLineStyle.Double ? 8 : 4, strokes.Length);
        Assert.All(strokes, path =>
        {
            TestSupport.Near(1, path.LineWidth);
            Assert.False(path.LineDashPattern?.Array.Any() == true);
        });
    }
}
