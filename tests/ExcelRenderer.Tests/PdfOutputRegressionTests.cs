using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PdfOutputRegressionTests
{
    [Theory]
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

    [Theory]
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
        Assert.Equal(runs ? 3 : 2, probe.Texts.Count);
        Assert.Equal(underline ? 2 : 0, probe.Paints.Count);
        if (!underline) { return; }
        Assert.All(probe.Paints, p => { Assert.Equal("S", p.Operation); Assert.Equal(2, p.Points.Length); });
        Point(probe.Paints[0].Points[0], x1, 24);
        Point(probe.Paints[0].Points[1], x2, 24);
        Point(probe.Paints[1].Points[0], x3, 48);
        Point(probe.Paints[1].Points[1], x4, 48);
    }

    [Fact]
    public void Pdf_finalized_glyph_path_uses_stored_origin()
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
        var first = PdfContentProbe.Read(OutputFixture.Pdf(Command(7, 3)));
        var shifted = PdfContentProbe.Read(OutputFixture.Pdf(Command(25, 17)));
        Assert.Empty(first.Texts);
        var a = Assert.Single(first.Paints);
        var b = Assert.Single(shifted.Paints);
        Assert.Equal("f", a.Operation);
        Assert.True(a.Points.Length > 3);
        Assert.Equal(a.Points.Length, b.Points.Length);
        for (var i = 0; i < a.Points.Length; i++) { Point(b.Points[i], a.Points[i].X + 18, a.Points[i].Y + 14); }
        // Absolute extent agrees with the fixed font's outline as well as relative movement.
        Near(17 + outline.Bounds.Left, a.Points.Min(p => p.X));
        Near(23 + outline.Bounds.Top, a.Points.Min(p => p.Y));
    }

    internal static void Point(PdfContentProbe.Point actual, double x, double y) { Near(x, actual.X); Near(y, actual.Y); }
    internal static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 0.05, $"Expected {expected}, got {actual}");
}
