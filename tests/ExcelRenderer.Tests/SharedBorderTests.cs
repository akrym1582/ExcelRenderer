using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Exercises shared cell borders independently of cell enumeration order.</summary>
public sealed class SharedBorderTests
{
    /// <summary>Both owners of a shared double edge must produce exactly two ink bands.</summary>
    [Theory]
    [InlineData(false, 144, 1)]
    [InlineData(true, 144, 1)]
    [InlineData(false, 288, 1)]
    [InlineData(true, 288, 1)]
    [InlineData(false, 288, 0.5)]
    [InlineData(true, 288, 0.5)]
    public void Shared_double_has_two_strokes(bool vertical, int dpi, double scale)
    {
        var side = new BorderSide(0.75 * scale, LineStyle: BorderLineStyle.Double);
        var first = Cell(new(10 * scale, 10 * scale, 30 * scale, 30 * scale), vertical ? new(Right: side) : new(Bottom: side));
        var second = Cell(vertical ? new(40 * scale, 10 * scale, 30 * scale, 30 * scale) : new(10 * scale, 40 * scale, 30 * scale, 30 * scale), vertical ? new(Left: side) : new(Top: side));
        var commands = Generate(first, second);
        using var output = new MemoryStream();
        new PngRenderer().RenderPage(commands, new PageSettings(80 * scale, 80 * scale), output, dpi);
        var bytes = output.ToArray();
        if (Environment.GetEnvironmentVariable("BORDER_VALIDATION_DIR") is { } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"double-{vertical}-{dpi}-{scale}.png"), bytes);
        }

        using var bitmap = SKBitmap.Decode(bytes);
        var bands = 0;
        var wasInk = false;
        var center = (int)Math.Round(40 * scale * dpi / 72);
        var sample = (int)Math.Round(25 * scale * dpi / 72);
        var radius = (int)Math.Ceiling(6 * scale * dpi / 72);
        for (var offset = center - radius; offset < center + radius; offset++)
        {
            var ink = (vertical ? bitmap.GetPixel(offset, sample) : bitmap.GetPixel(sample, offset)).Red < 180;
            if (ink && !wasInk)
            {
                bands++;
            }

            wasInk = ink;
        }

        Assert.Equal(2, bands);
        Assert.Single(commands.OfType<DrawLineCommand>());
        Assert.Equal(commands, Generate(second, first));
    }

    /// <summary>A blank opposing side never erases the only defined border.</summary>
    [Fact]
    public void One_sided_double_uses_the_same_centered_geometry()
    {
        var side = new BorderSide(0.75, LineStyle: BorderLineStyle.Double);
        var first = Cell(new(10, 10, 30, 30), new(Right: side));
        var blank = Cell(new(40, 10, 30, 30), new());
        var opposite = blank with { Cell = blank.Cell with { Style = blank.Cell.Style with { Border = new(Left: side) } } };
        Assert.Equal(Generate(first, opposite), Generate(first, blank));
        var line = Assert.IsType<DrawLineCommand>(Assert.Single(Generate(first, blank)));
        Assert.Equal((40d, 10d, 40d, 40d), (line.X1, line.Y1, line.X2, line.Y2));
    }

    /// <summary>Position, rather than brightness or enumeration order, breaks equal-style ties.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void White_can_win_an_equal_width_color_conflict(bool vertical)
    {
        var white = new BorderSide(1, new(255, 255, 255));
        var black = new BorderSide(1, new(0, 0, 0));
        var first = Cell(new(10, 10, 30, 30), vertical ? new(Right: white) : new(Bottom: white));
        var second = Cell(vertical ? new(40, 10, 30, 30) : new(10, 40, 30, 30), vertical ? new(Left: black) : new(Top: black));
        Assert.Equal(white, Assert.IsType<DrawLineCommand>(Assert.Single(Generate(first, second))).Style);
        Assert.Equal(Generate(first, second), Generate(second, first));
    }

    /// <summary>Merged fragments compete only on their actual overlapping intervals.</summary>
    [Fact]
    public void Merged_partial_edges_preserve_blank_intervals_and_local_winners()
    {
        var thin = new BorderSide(0.5);
        var thick = new BorderSide(2);
        var merged = Cell(new(0, 0, 20, 30), new()) with
        {
            MergedBorders = [new(new(0, 0, 20, 10), new(Right: thin)), new(new(0, 20, 20, 10), new(Right: thin))],
        };
        var neighbor = Cell(new(20, 5, 10, 20), new(Left: thick));
        var lines = Generate(merged, neighbor).OfType<DrawLineCommand>().ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Equal(new[] { (0d, 5d, 0.5), (5d, 25d, 2d), (25d, 30d, 0.5) }, lines.Select(line => (line.Y1, line.Y2, line.Style.Width)));
        Assert.Equal(Generate(merged, neighbor), Generate(neighbor, merged));
        Assert.Equal(2, Generate(merged).Count);
    }

    /// <summary>A very narrow column is not folded into the neighboring boundary.</summary>
    [Fact]
    public void Narrow_column_keeps_distinct_edges()
    {
        var side = new BorderSide();
        var cell = Cell(new(20, 10, 0.0000001, 30), new(Left: side, Right: side));
        var lines = Generate(cell).OfType<DrawLineCommand>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.NotEqual(lines[0].X1, lines[1].X1);
    }

    /// <summary>A clipped patterned edge retains its source start and therefore its dash phase.</summary>
    [Fact]
    public void Split_dashed_edge_keeps_its_original_phase()
    {
        var dash = new BorderSide(1, LineStyle: BorderLineStyle.Dashed);
        var longEdge = Cell(new(0, 0, 20, 40), new(Right: dash));
        var stronger = Cell(new(20, 7, 10, 10), new(Left: new(2)));
        var commands = Generate(longEdge, stronger);
        var viewports = commands.OfType<DrawViewportCommand>().ToArray();
        Assert.Equal(2, viewports.Length);
        Assert.All(viewports, viewport =>
        {
            var line = Assert.IsType<DrawLineCommand>(Assert.Single(viewport.Commands));
            Assert.Equal((0d, 40d), (line.Y1, line.Y2));
        });
        Assert.Equal(new[] { (0d, 7d), (17d, 23d) }, viewports.Select(viewport => (viewport.Clip.Y, viewport.Clip.Height)));
    }

    private static RenderCell Cell(ReportRect bounds, BorderStyle border) => new(new(null, CellStyle.Default with { Border = border }), bounds);

    private static IReadOnlyList<DrawCommand> Generate(params RenderCell[] cells) => new DrawCommandGeneratorPass().Generate(new([new(1, cells)]));
}
