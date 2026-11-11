using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class PaintColorOptimizationTests
{
    [Fact]
    public void Fill_compares_every_rgba_component_without_reading_native_color()
    {
        var counts = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => counts[name] = counts.GetValueOrDefault(name) + value;
        try
        {
            using var resources = new SkiaPaintResources();
            ReportColor[] colors = [new(1, 2, 3, 4), new(1, 2, 3, 5), new(2, 2, 3, 5), new(2, 3, 3, 5), new(2, 3, 4, 5), new(1, 2, 3, 4)];
            SKPaint? first = null;
            foreach (var color in colors)
            {
                var paint = resources.Fill(color);
                first ??= paint;
                Assert.Same(first, paint);
                Assert.Equal(new SKColor(color.Red, color.Green, color.Blue, color.Alpha), paint.Color);
                Assert.Same(paint, resources.Fill(color));
            }

            Assert.Equal(colors.Length, counts["paintColorSetters"]);
            Assert.Equal(colors.Length, counts["paintColorSettersSkipped"]);
            Assert.Equal(1, counts["paintCreated"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Stroke_color_skip_preserves_width_dash_and_separate_fill_state()
    {
        using var resources = new SkiaPaintResources();
        var color = new ReportColor(20, 30, 40, 50);
        var stroke = resources.Stroke(new(2, color, BorderLineStyle.Dashed));
        Assert.Equal(SKPaintStyle.Stroke, stroke.Style);
        Assert.NotNull(stroke.PathEffect);
        var fill = resources.Fill(color);
        Assert.NotSame(fill, stroke);
        Assert.Equal(SKPaintStyle.Fill, fill.Style);
        Assert.Null(fill.PathEffect);
        Assert.Same(stroke, resources.Stroke(new(4, color, BorderLineStyle.Solid)));
        Assert.Equal(4, stroke.StrokeWidth);
        Assert.Null(stroke.PathEffect);
        Assert.Equal(new SKColor(20, 30, 40, 50), stroke.Color);
        resources.Fill(new(20, 30, 40, 51));
        Assert.Equal(new SKColor(20, 30, 40, 50), stroke.Color);
        Assert.Equal(SKColors.Black, resources.Stroke(new(1)).Color);
        Assert.Equal(SKColors.Black, resources.Stroke(new(3, new(0, 0, 0))).Color);
    }

    [Fact]
    public void Restored_temporary_emoji_color_keeps_owner_state_valid_after_exception()
    {
        using var resources = new SkiaPaintResources();
        var color = new ReportColor(120, 20, 30, 100);
        var paint = resources.Fill(color);
        var original = paint.Color;
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            try
            {
                paint.Color = SKColors.White;
                throw new InvalidOperationException();
            }
            finally
            {
                paint.Color = original;
            }
        }));
        Assert.Same(paint, resources.Fill(color));
        Assert.Equal(original, paint.Color);
        Assert.Equal(SKColors.White, resources.Fill(new(255, 255, 255)).Color);
        Assert.Equal(original, resources.Fill(color).Color);
    }

    [Fact]
    public void Independent_owners_and_recreation_do_not_share_cached_colors()
    {
        using var first = new SkiaPaintResources();
        using var second = new SkiaPaintResources();
        var color = new ReportColor(10, 20, 30, 40);
        var oldFill = first.Fill(color);
        var oldStroke = first.Stroke(new(2, color));
        Assert.NotSame(oldFill, second.Fill(color));
        second.Fill(new(50, 60, 70, 80));
        Assert.Equal(new SKColor(10, 20, 30, 40), oldFill.Color);
        first.Dispose();
        first.Dispose();
        Assert.NotSame(oldFill, first.Fill(color));
        Assert.NotSame(oldStroke, first.Stroke(new(2, color)));
        Assert.Equal(new SKColor(10, 20, 30, 40), first.Fill(color).Color);
        Assert.Equal(new SKColor(10, 20, 30, 40), first.Stroke(new(2, color)).Color);
    }
}
