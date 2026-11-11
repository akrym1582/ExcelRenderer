using System.Text;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using ExcelRenderer.TestFixtures;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class MainPerformanceOptimizationTests
{
    [Theory]
    [InlineData(false, 80)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public void Unwrapped_lines_skip_outline_paragraphs_and_preserve_cancellation(bool wrap, double width)
    {
        using var cancellation = new CancellationTokenSource();
        using var resources = new ConversionFontResources(cancellation.Token);
        var measurer = new SkiaTextMeasurer(Manager(), true);
        var counts = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => counts[name] = counts.GetValueOrDefault(name) + value;
        try
        {
            var result = measurer.Layout("first  \r\n\nlast ", new(), width, wrap);
            Assert.Equal(new[] { "first  ", "", "last " }, result.Lines.Select(line => line.Text));
            Assert.Equal(new[] { true, true, false }, result.Lines.Select(line => line.ExplicitBreak));
            Assert.False(counts.ContainsKey("outlineParagraphs"));
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => measurer.Layout("first  \nnew", new(), width, wrap));
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Outline_matches_compatibility_at_exact_wrap_boundaries()
    {
        var manager = Manager();
        using var resources = new ConversionFontResources();
        var fast = new SkiaTextMeasurer(manager, true);
        var original = new SkiaTextMeasurer(new CompatibilityManager(manager));
        var random = new Random(43119);
        string[] elements = ["a", " ", "日", "本", "e\u0301", "😀", "👩‍👩‍👧", "\r\n", "\n", "\t", "\u0378", "葛\U000E0100"];
        var texts = new List<string> { "", "\n", "\n\n", "abc \r\n\n", "日 本   ", "a" + new string('\u0301', 256) };
        for (var sample = 0; sample < 50; sample++)
        {
            var text = new StringBuilder();
            for (var index = 0; index < random.Next(1, 25); index++) text.Append(elements[random.Next(elements.Length)]);
            texts.Add(text.ToString());
        }

        foreach (var text in texts)
        foreach (var size in new[] { 0d, 7.25, 11, 28 })
        foreach (var wrap in new[] { false, true })
        {
            var style = new FontStyle(Size: size);
            var boundary = original.Layout("a日", style, 100, false).Size.Width;
            foreach (var width in new[] { -1d, 0, Math.BitDecrement(boundary), boundary, Math.BitIncrement(boundary), 37.5 })
            {
                EqualLayout(original.Layout(text, style, width, wrap), fast.Layout(text, style, width, wrap));
            }
        }
    }

    [Fact]
    public void Unique_long_paragraphs_select_glyphs_once_without_cache_hits()
    {
        var manager = Manager();
        using var resources = new ConversionFontResources();
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var measurer = new SkiaTextMeasurer(manager, true);
            var text = string.Concat(Enumerable.Repeat("日本abc ", 1000));
            Assert.True(measurer.Layout(text, new(), 80, true).Lines.Count > 100);
            Assert.Equal(1, metrics["textGlyphCalls"]);
            Assert.Equal(text.Length, metrics["textGlyphCharacters"]);
            Assert.False(metrics.ContainsKey("textLayoutCacheHits"));
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    public void Normal_cache_uses_only_metric_inputs_and_evicts_one_entry(int count)
    {
        using var resources = new ConversionFontResources();
        var manager = Manager();
        var measurer = new SkiaTextMeasurer(manager, true);
        var style = new FontStyle();
        var first = measurer.Layout("heading", style, 10, false);
        Assert.Same(first, measurer.Layout("heading", style with { Underline = true, Color = new(100, 20, 30) }, 100, false));
        Assert.NotSame(first, measurer.Layout("heading", style with { Size = 12 }, 100, false));
        manager.Register("Noto Sans JP", FontPath);
        Assert.NotSame(first, measurer.Layout("heading", style, 10, false));
        measurer = new(manager, true);
        var entries = 0d;
        var evictions = 0d;
        var maximumUnits = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "textLayoutCacheEntries.current") entries = value;
            if (name == "textLayoutCacheEvictions") evictions += value;
            if (name.EndsWith("RetainedUnits.max", StringComparison.Ordinal)) maximumUnits = Math.Max(maximumUnits, value);
        };
        try
        {
            for (var index = 0; index < count; index++) measurer.Layout(index.ToString(), style, 100, false);
            Assert.Equal(Math.Min(count, 512), entries);
            Assert.Equal(Math.Max(0, count - 512), evictions);
            Assert.InRange(maximumUnits, 1, 131072);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Rich_cache_keeps_decorations_and_bounds_zero_length_run_keys()
    {
        using var resources = new ConversionFontResources();
        var measurer = new SkiaTextMeasurer(Manager(), true);
        var red = new FontStyle(Color: new(180, 20, 30), Underline: true);
        var blue = red with { Color = new(20, 30, 180), Underline = false };
        var first = measurer.Layout([new("text", red)], new(), 100, false);
        Assert.Same(first, measurer.Layout([new("text", red)], new(), 200, false));
        var second = measurer.Layout([new("text", blue)], new(), 100, false);
        Assert.NotSame(first, second);
        Assert.Equal(red, first.Lines[0].Runs[0].Font);
        Assert.Equal(blue, second.Lines[0].Runs[0].Font);
        var bypasses = 0d;
        ConversionMetrics.Observer = (name, value) => { if (name == "richTextLayoutCacheBypasses") bypasses += value; };
        try
        {
            var huge = Enumerable.Repeat(new CellTextRun("", red), 131073).ToArray();
            Assert.Empty(measurer.Layout(huge, new(), 100, false).Lines);
            Assert.Equal(1, bypasses);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Standalone_rich_cache_does_not_keep_layout_alive()
    {
        var manager = Manager();
        var measurer = new SkiaTextMeasurer(manager);
        var reference = WeakLayout(measurer);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.TryGetTarget(out _));
        GC.KeepAlive(measurer);
        GC.KeepAlive(manager);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_reuses_paints_without_changing_rich_and_border_pixels(bool paths)
    {
        using var resources = new ConversionFontResources();
        var manager = Manager();
        var measurer = new SkiaTextMeasurer(manager);
        var commands = new List<DrawCommand>();
        for (var index = 0; index < 16; index++)
        {
            var font = new FontStyle(Size: 9, Underline: true, Color: new(20, 30, 180));
            var rich = measurer.Layout([new("A", font with { Color = new(180, 20, 30) }), new("B", font)], font, 100, false);
            commands.Add(new FillRectangleCommand(1, new(0, index * 15, 150, 15), new(240, 240, 250, 180)));
            commands.Add(new DrawTextCommand(1, new(5, index * 15, 100, 15), "AB", CellStyle.Default with { Font = font }) { TextLayout = rich });
            commands.Add(new DrawTextCommand(1, new(40, index * 15, 100, 15), "plain", CellStyle.Default with { Font = font }) { TextLayout = measurer.Layout("plain", font, 100, false) });
            commands.Add(new DrawLineCommand(1, 0, index * 15, 145, index * 15, new(0.75, new(40, 90, 20), (BorderLineStyle)(index % 6))));
        }

        using var expected = new SKBitmap(150, 250);
        using var actual = new SKBitmap(150, 250);
        using (var canvas = new SKCanvas(expected))
        {
            canvas.Clear(SKColors.White);
            foreach (var command in commands)
            {
                using var isolated = new SkiaDrawingContext(paths, manager);
                isolated.Execute(canvas, command);
            }
        }

        var created = 0d;
        var disposed = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "paintCreated") created += value;
            if (name == "paintDisposed") disposed += value;
        };
        try
        {
            using (var context = new SkiaDrawingContext(paths, manager))
            using (var canvas = new SKCanvas(actual))
            {
                canvas.Clear(SKColors.White);
                foreach (var command in commands) context.Execute(canvas, command);
            }

            Assert.Equal(expected.Pixels, actual.Pixels);
            Assert.Equal(2, created);
            Assert.Equal(created, disposed);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Preflight_preserves_selected_page_descriptors_and_one_payload(bool trim, bool warnings, bool lightweight)
    {
        using var input = RetainedWorkbook.Open("Phase3MemoryTests.Selected_pages_preflight_and_render_with_one_active_payload.0BA00D47150071DC.xlsx");
        using var output = new MemoryStream();
        var counts = new Dictionary<string, double>();
        var active = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            counts[name] = counts.GetValueOrDefault(name) + value;
            if (name == "pagePayloadActive")
            {
                active += value;
                Assert.InRange(active, 0, 1);
            }
        };
        try
        {
            var result = await ExcelConverter.RenderAsync(input, new()
            {
                FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [FontPath] },
                Selection = new() { Pages = [3, 1, 3] },
                Trim = new() { Enabled = trim },
                DiagnosticOptions = new() { EnableLayoutWarnings = warnings },
            }, new SingleStreamOutputSink(output));
            Assert.Equal(2, result.Pages.Count);
            Assert.All(result.Pages, page => Assert.NotEmpty(page.SourceRegions!));
            Assert.Equal(lightweight ? 2 : 0, counts.GetValueOrDefault("mainTextPreflightBuilds"));
            Assert.Equal(lightweight ? 2 : 4, counts["mainFullPageBuilds"]);
            Assert.Equal(counts["paintCreated"], counts["paintDisposed"]);
            Assert.Equal(0, active);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<TextLayoutResult> WeakLayout(SkiaTextMeasurer measurer) =>
        new(measurer.Layout([new("standalone", new(Color: new(30, 80, 120)))], new(), 100, false));

    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");

    private static FontManager Manager() => new(new()
    {
        AllowSystemFonts = false,
        UseFontPack = false,
        Registrations = [new("Noto Sans JP", FontPath)],
        FallbackFamilies = ["Noto Sans JP"],
    });

    private static void EqualLayout(TextLayoutResult expected, TextLayoutResult actual)
    {
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.EffectiveFontSize, actual.EffectiveFontSize);
        Assert.Equal(expected.Lines.Count, actual.Lines.Count);
        for (var index = 0; index < expected.Lines.Count; index++)
        {
            var a = expected.Lines[index];
            var b = actual.Lines[index];
            Assert.Equal((a.Text, a.Width, a.Height, a.Baseline, a.Ascent, a.Descent, a.Leading, a.ExplicitBreak),
                (b.Text, b.Width, b.Height, b.Baseline, b.Ascent, b.Descent, b.Leading, b.ExplicitBreak));
            Assert.Equal(a.Runs.Count, b.Runs.Count);
            for (var runIndex = 0; runIndex < a.Runs.Count; runIndex++)
            {
                var x = a.Runs[runIndex];
                var y = b.Runs[runIndex];
                Assert.Equal((x.Run.Text, x.Run.SourceText, x.Run.Utf16Start, x.Run.Font.FaceId, x.X, x.Advance, x.Font),
                    (y.Run.Text, y.Run.SourceText, y.Run.Utf16Start, y.Run.Font.FaceId, y.X, y.Advance, y.Font));
                Assert.Equal(x.Glyphs!.FontIdentity, y.Glyphs!.FontIdentity);
                Assert.Equal(x.Glyphs.Glyphs, y.Glyphs.Glyphs);
                Assert.Equal(x.Glyphs.Source, y.Glyphs.Source);
                Assert.Equal(x.Glyphs.Clusters, y.Glyphs.Clusters);
                Assert.Equal(x.Glyphs.Advances, y.Glyphs.Advances);
            }
        }
    }

    private sealed class CompatibilityManager(FontManager manager) : IFontManager
    {
        public ResolvedFont Resolve(FontRequest request) => manager.Resolve(request);

        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request) => manager.ResolveTextRuns(text, request);
    }
}
