using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using Xunit;
using ImageResources = ExcelRenderer.Core.Rendering.ImageResources;
using ImageAlphaKind = ExcelRenderer.Core.Rendering.ImageAlphaKind;

namespace ExcelRenderer.Tests;

public sealed class RenderFinalizationTests
{
    [Fact]
    public void Complete_nested_shape_text_preserves_the_supplied_command_and_page_identity()
    {
        var service = new SkiaTextMeasurer(PostScriptTests.Manager());
        var text = Assert.IsType<DrawTextCommand>(CommandTextFinalizer.Finalize([PostScriptTests.Text("supplied 辻\U000E0102")], service, default).Single());
        var shape = new DrawShapeCommand(1, new(0, 0, 200, 100), new(new(1, 1), 0, 0, 200, 100, Model.ShapeKind.Ellipse,
            new(null, null), new("ignored", new("Noto Sans JP", 9), Model.HorizontalAlignment.Left, Model.VerticalAlignment.Top, true, 0, 0, 0, 0), 0, 0)) { TextCommand = text };
        DrawCommand[] page = [new DrawViewportCommand(1, [shape], new(0, 0, 200, 100), 0, 0)];
        var finalized = CommandTextFinalizer.FinalizePage(page, service, default);
        Assert.Same(page, finalized);
        Assert.Same(shape, Assert.IsType<DrawViewportCommand>(finalized[0]).Commands[0]);
        Assert.Same(text, shape.TextCommand);
    }

    [Fact]
    public void Missing_glyphs_are_completed_without_replacing_custom_line_or_run_geometry()
    {
        var service = new SkiaTextMeasurer(PostScriptTests.Manager());
        var text = Assert.IsType<DrawTextCommand>(CommandTextFinalizer.Finalize([PostScriptTests.Text("A辻\U000E0102")], service, default).Single());
        var original = text.TextLayout!;
        var line = original.Lines[0] with { Width = 123.25, Height = 31.5, Baseline = 23.125, Runs = original.Lines[0].Runs.Select(run => run with { X = run.X + 5.5, Glyphs = null }).ToArray() };
        text = text with { TextLayout = original with { Lines = [line] } };
        var completed = Assert.IsType<DrawTextCommand>(CommandTextFinalizer.Finalize([text], service, default).Single());
        var result = completed.TextLayout!.Lines[0];
        Assert.Equal(line.Width, result.Width);
        Assert.Equal(line.Height, result.Height);
        Assert.Equal(line.Baseline, result.Baseline);
        Assert.Equal(line.Runs.Select(run => (run.X, run.Advance, run.Run.SourceText)), result.Runs.Select(run => (run.X, run.Advance, run.Run.SourceText)));
        Assert.All(result.Runs, run => Assert.NotNull(run.Glyphs));
        Assert.Same(completed, CommandTextFinalizer.Finalize([completed], service, default).Single());
    }

    [Theory]
    [InlineData(255, (int)ImageAlphaKind.Opaque)]
    [InlineData(0, (int)ImageAlphaKind.Transparent)]
    [InlineData(128, (int)ImageAlphaKind.Partial)]
    [InlineData(-1, (int)ImageAlphaKind.BinaryMask)]
    public void Alpha_metadata_survives_native_eviction_without_sharing_different_byte_arrays(int alpha, int expected)
    {
        using var bitmap = new global::SkiaSharp.SKBitmap(3, 2);
        bitmap.Erase(new global::SkiaSharp.SKColor(40, 80, 120, (byte)Math.Max(0, alpha)));
        if (alpha == -1) bitmap.SetPixel(0, 0, global::SkiaSharp.SKColors.Red);
        using var image = global::SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
        var bytes = encoded.ToArray();
        using var resources = new ImageResources(1);
        var scans = 0d;
        var hits = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "imageAlphaScans") scans += value;
            if (name == "imageAlphaCacheHits") hits += value;
        };
        try
        {
            for (var pass = 0; pass < 3; pass++)
            {
                using var lease = resources.AcquireImage(bytes, out var failure);
                Assert.Null(failure);
                Assert.Equal((ImageAlphaKind)expected, resources.InspectAlpha(bytes, lease!.Value, default));
            }

            Assert.InRange(scans, 0, 1);
            Assert.Equal(2, hits);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            using var again = resources.AcquireImage(bytes, out _);
            Assert.Throws<OperationCanceledException>(() => resources.InspectAlpha(bytes, again!.Value, cancelled.Token));
            var separate = bytes.ToArray();
            using var other = resources.AcquireImage(separate, out _);
            Assert.Equal((ImageAlphaKind)expected, resources.InspectAlpha(separate, other!.Value, default));
            Assert.Equal(2, hits);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Pdf_collection_registers_transparent_nested_text_without_decoding_images_or_painting()
    {
        var manager = PostScriptTests.Manager();
        var text = Assert.IsType<DrawTextCommand>(CommandTextFinalizer.Finalize([PostScriptTests.Text("透明 A") with
        {
            Style = Model.CellStyle.Default with { Font = new("Noto Sans JP", 12, Color: new(0, 0, 0, 0)) },
        }], new SkiaTextMeasurer(manager), default).Single());
        using var writer = new SkiaPdfDocumentWriter(manager, default);
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            writer.Collect([new DrawViewportCommand(1, [text, new DrawImageCommand(1, new(0, 0, 10, 10), [1, 2, 3])], new(0, 0, 100, 100), 0, 0)]);
            Assert.True(metrics["pdfFontRunsCollected"] > 0);
            Assert.False(metrics.ContainsKey("imageDecode"));
            Assert.False(metrics.ContainsKey("legacyTextPaints"));
            writer.Prepare();
            writer.Append(new(100, 100), new(0, "test", 1, 1, 1, 100, 100), [text], [], _ => { });
            using var output = new MemoryStream();
            writer.Complete(output);
            Assert.True(output.Length > 0);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Trim_bounds_include_the_ordinary_font_underline_below_the_glyph_baseline()
    {
        var manager = PostScriptTests.Manager();
        using var resources = new ConversionFontResources();
        var text = Assert.IsType<DrawTextCommand>(CommandTextFinalizer.Finalize([PostScriptTests.Text("A") with
        {
            Style = Model.CellStyle.Default with { Font = new("Noto Sans JP", 12, Underline: true), VerticalAlignment = Model.VerticalAlignment.Top },
        }], new SkiaTextMeasurer(manager), default).Single());
        var baseline = TextLayoutPlacement.Place(text.TextLayout!, text.Bounds, text.Style.HorizontalAlignment, text.Style.VerticalAlignment)[0].Baseline;
        var ink = DrawCommandBounds.Get([text], new(0, 0, 600, 800), manager)!.Value;
        Assert.True(ink.Y + ink.Height >= baseline + 1.5);
    }

    [Fact]
    public void Finalized_glyph_ink_is_reused_after_the_original_resource_scope_has_closed()
    {
        var manager = PostScriptTests.Manager();
        DrawTextCommand text;
        using (var resources = new ConversionFontResources())
        {
            text = Assert.IsType<DrawTextCommand>(CommandTextFinalizer.Finalize([PostScriptTests.Text("辻\U000E0102 ABC")], new SkiaTextMeasurer(manager), default).Single());
        }

        using var replay = new ConversionFontResources();
        var hits = 0d;
        ConversionMetrics.Observer = (name, value) => { if (name == "glyphInkCacheHits") hits += value; };
        try
        {
            var first = DrawCommandBounds.Get([text], new(0, 0, 600, 800), manager);
            Assert.NotNull(first);
            Assert.Equal(first, DrawCommandBounds.Get([text], new(0, 0, 600, 800), manager));
            Assert.True(hits > 0);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }
}
