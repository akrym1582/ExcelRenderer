using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.SkiaSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class DrawingStateRegressionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Skia_legacy_restores_canvas_after_drawing_failure(int rotation, bool callerState) => Skia(false, rotation, callerState);

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Skia_finalized_restores_canvas_after_drawing_failure(int rotation, bool callerState) => Skia(true, rotation, callerState);

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Pdf_finalized_restores_graphics_after_drawing_failure(int rotation, bool callerState) => Pdf(true, rotation, callerState);

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Pdf_legacy_restores_graphics_after_drawing_failure(int rotation, bool callerState) => Pdf(false, rotation, callerState);

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void Pdf_legacy_ordinary_text_preserves_caller_transform_and_clip(int rotation)
    {
        using var document = new PdfDocument();
        var page = document.AddPage(); page.Width = XUnit.FromPoint(120); page.Height = XUnit.FromPoint(80);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.TranslateTransform(4, 3);
            graphics.IntersectClip(new XRect(0, 0, 116, 77));
            var before = graphics.Transform;
            new PdfSharpTextPainter(null).Paint(graphics, Command(false, rotation));
            Assert.Equal(before, graphics.Transform);
            graphics.DrawRectangle(XBrushes.Red, new XRect(110, 72, 4, 4));
        }
        using var output = new MemoryStream(); document.Save(output, false);
        var probe = PdfContentProbe.Read(output.ToArray());
        Assert.Single(probe.Texts);
        var sentinel = Assert.Single(probe.Paints);
        Assert.Single(sentinel.Clips);
        PdfOutputRegressionTests.Near(114, sentinel.Points.Min(p => p.X));
        PdfOutputRegressionTests.Near(75, sentinel.Points.Min(p => p.Y));
        PdfOutputRegressionTests.Near(118, sentinel.Points.Max(p => p.X));
        PdfOutputRegressionTests.Near(79, sentinel.Points.Max(p => p.Y));
        Assert.Equal(rotation == 0 ? 2 : 3, probe.AppliedClips.Count);
    }

    private static DrawTextCommand Command(bool finalized, int rotation)
    {
        using var face = OutputFixture.Typeface();
        using var font = new SKFont(face, 9);
        // A known, valid space glyph has no outline. Loading and measurement succeed; actual path drawing throws.
        var glyph = font.GetGlyphs(" ")[0];
        Assert.NotEqual((ushort)0, glyph);
        using var path = font.GetGlyphPath(glyph);
        Assert.True(path is null || path.IsEmpty);
        return new(1, new(10, 20, 100, 50), "A", CellStyle.Default with
        {
            Font = new("Noto Sans JP", 12), WrapText = true, TextRotation = rotation,
        })
        {
            TextLayout = finalized ? new TextLayoutResult(new(20, 12),
                [new("A", 20, 12, 9, [new(new("A", OutputFixture.Face) { GlyphId = glyph }, 0, 10)], false)])
                { EffectiveFontSize = 9 } : null,
        };
    }

    private static void Skia(bool finalized, int rotation, bool callerState)
    {
        using var bitmap = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        if (callerState)
        {
            canvas.Translate(4, 3);
            canvas.ClipRect(new(0, 0, 116, 77));
        }
        var saveCount = canvas.SaveCount;
        var originalMatrix = canvas.TotalMatrix;
        var originalClip = canvas.DeviceClipBounds;
        var manager = new DrawingFailureManager();
        var painter = new SkiaTextPainter(true, manager, () =>
        {
            Assert.Equal(saveCount + (rotation == 0 ? 1 : 2), canvas.SaveCount);
            Assert.NotEqual(originalClip, canvas.DeviceClipBounds);
            manager.Armed = true;
        });
        var exception = Assert.Throws<InvalidOperationException>(() => painter.Paint(canvas, Command(finalized, rotation)));
        if (!finalized)
        {
            Assert.Same(manager.Failure, exception);
            Assert.Equal("ResolveTextRuns during drawing", manager.FailedOperation);
            Assert.True(manager.SuccessfulResolutions > 0);
        }
        else { Assert.Contains("アウトライン", exception.Message); }
        Assert.Equal(saveCount, canvas.SaveCount);
        Assert.Equal(originalMatrix, canvas.TotalMatrix);
        Assert.Equal(originalClip, canvas.DeviceClipBounds);
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(new SKRect(110, 72, 114, 76), paint);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(callerState ? 115 : 111, callerState ? 76 : 73));
        Assert.Equal(SKColors.White, bitmap.GetPixel(callerState ? 111 : 115, callerState ? 73 : 77));
        // A marker outside the caller clip must still be clipped after failure.
        if (callerState)
        {
            canvas.DrawRect(new SKRect(-4, -3, 0, 0), paint);
            Assert.Equal(SKColors.White, bitmap.GetPixel(1, 1));
        }
    }

    private static void Pdf(bool finalized, int rotation, bool callerState)
    {
        using var document = new PdfDocument();
        var page = document.AddPage(); page.Width = XUnit.FromPoint(120); page.Height = XUnit.FromPoint(80);
        var manager = new DrawingFailureManager();
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            if (callerState)
            {
                graphics.TranslateTransform(4, 3);
                graphics.IntersectClip(new XRect(0, 0, 116, 77));
            }
            var original = graphics.Transform;
            var painter = new PdfSharpTextPainter(manager, () => manager.Armed = true);
            var exception = Assert.Throws<InvalidOperationException>(() => painter.Paint(graphics, Command(finalized, rotation)));
            if (!finalized)
            {
                Assert.Same(manager.Failure, exception);
                Assert.Equal("ResolveTextRuns during drawing", manager.FailedOperation);
                Assert.True(manager.SuccessfulResolutions > 0);
            }
            else { Assert.Contains("アウトライン", exception.Message); }
            Assert.Equal(original, graphics.Transform);
            graphics.DrawRectangle(XBrushes.Red, new XRect(110, 72, 4, 4));
        }
        using var output = new MemoryStream(); document.Save(output, false);
        var probe = PdfContentProbe.Read(output.ToArray());
        var sentinel = Assert.Single(probe.Paints);
        Assert.Equal("f", sentinel.Operation);
        Assert.Equal(4, sentinel.Points.Length);
        var x = callerState ? 114 : 110; var y = callerState ? 75 : 72;
        Assert.Equal(x, sentinel.Points.Min(p => p.X), 2); Assert.Equal(y, sentinel.Points.Min(p => p.Y), 2);
        Assert.Equal(x + 4, sentinel.Points.Max(p => p.X), 2); Assert.Equal(y + 4, sentinel.Points.Max(p => p.Y), 2);
        Assert.Equal(callerState ? 1 : 0, sentinel.Clips.Length);
        if (callerState)
        {
            var clip = sentinel.Clips[0];
            Assert.Equal(4, clip.Min(p => p.X), 2); Assert.Equal(3, clip.Min(p => p.Y), 2);
            Assert.Equal(120, clip.Max(p => p.X), 2); Assert.Equal(80, clip.Max(p => p.Y), 2);
        }
        Assert.Equal((callerState ? 1 : 0) + (rotation == 0 ? 1 : 2), probe.AppliedClips.Count);
        // The inner failed text clip is present in real content but no longer active when sentinel paints.
        Assert.Contains(probe.AppliedClips, c => Math.Abs(c.Min(p => p.X) - (callerState ? 14 : 10)) < 0.05 &&
            Math.Abs(c.Min(p => p.Y) - (callerState ? 23 : 20)) < 0.05);
    }

    private sealed class DrawingFailureManager : IFontManager
    {
        internal bool Armed { get; set; }
        internal string? FailedOperation { get; private set; }
        internal int SuccessfulResolutions { get; private set; }
        internal InvalidOperationException Failure { get; } = new("Controlled drawing failure");
        public ResolvedFont Resolve(FontRequest request) => OutputFixture.Face;
        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request)
        {
            if (Armed) { FailedOperation = "ResolveTextRuns during drawing"; throw Failure; }
            SuccessfulResolutions++;
            using var face = OutputFixture.Typeface(); using var font = new SKFont(face, 12);
            return [new(text, OutputFixture.Face) { GlyphId = font.GetGlyphs("A")[0] }];
        }
    }
}
