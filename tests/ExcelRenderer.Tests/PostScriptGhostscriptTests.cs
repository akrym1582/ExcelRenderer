using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.PostScript;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PostScriptGhostscriptTests
{
    internal static readonly string? Executable = FindExecutable();
    private static string Artifacts => Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "PostScript");

    [GhostscriptFact]
    public async Task Type42_japanese_banks_ivs_and_supplementary_execute_without_substitution()
    {
        Directory.CreateDirectory(Artifacts);
        var text = string.Concat(Enumerable.Range(0x3041, 86).Concat(Enumerable.Range(0x4e00, 250)).Select(char.ConvertFromUtf32));
        var commands = new DrawCommand[] { PostScriptTests.Text("ABC 123 () \\ かな 漢字", y: 10), PostScriptTests.Text("辻\U000E0102 葛\U000E0102 \U0002000B", y: 60), PostScriptTests.Text(text, y: 110) with { Style = CellStyle.Default with { Font = new("Noto Sans JP", 10), WrapText = true } } };
        var ps = PostScriptTests.Render(commands);
        var file = Path.Combine(Artifacts, "fonts.ps");
        await File.WriteAllBytesAsync(file, ps);
        await Run("-sDEVICE=nullpage", file);
        await Run("-sDEVICE=png16m", "-r72", "-sOutputFile=" + Path.Combine(Artifacts, "fonts.png"), file);
        var extracted = Path.Combine(Artifacts, "fonts.txt");
        await Run("-sDEVICE=txtwrite", "-dTextFormat=3", "-sOutputFile=" + extracted, file);
        var actual = await File.ReadAllTextAsync(extracted);
        Assert.Contains("ABC 123 () \\ かな 漢字", actual);
        Assert.Contains(text, Regex.Replace(actual, @"\s", ""));
        await File.WriteAllTextAsync(Path.Combine(Artifacts, "extraction.json"), JsonSerializer.Serialize(new
        {
            standardGlyphs = true,
            ivsSequenceExtracted = actual.Contains("辻\U000E0102", StringComparison.Ordinal),
            supplementaryExtracted = actual.Contains("\U0002000B", StringComparison.Ordinal),
            sourceMetadataRestored = PostScriptTests.Metadata(Encoding.ASCII.GetString(ps)).Any(record => record.Kind == 1 && record.Text.Contains("\U000E0102", StringComparison.Ordinal)),
        }));
        var standard = Path.Combine(Artifacts, "standard.ps");
        const string source = "ABC 123 () \\ かな 漢字";
        await File.WriteAllBytesAsync(standard, PostScriptTests.Render(PostScriptTests.Text(source)));
        var standardText = Path.Combine(Artifacts, "standard.txt");
        await Run("-sDEVICE=txtwrite", "-dTextFormat=3", "-sOutputFile=" + standardText, standard);
        Assert.Equal(source, (await File.ReadAllTextAsync(standardText)).Trim());
    }

    [GhostscriptFact]
    public async Task Finalized_commands_compare_with_png_and_detect_removed_glyph_and_displaced_text()
    {
        Directory.CreateDirectory(Artifacts);
        using var resources = new RenderResourceSession();
        var manager = PostScriptTests.Manager();
        var measurer = new SkiaTextMeasurer(manager);
        var commands = Fixture();
        var finalized = CommandTextFinalizer.Finalize(commands, measurer, default).ToArray();
        var page = new PageSettings(360, 400);
        var psPath = Path.Combine(Artifacts, "drawing.ps");
        using (var output = File.Create(psPath)) new PostScriptRenderer(manager).Render(finalized, page, output);
        await Run("-sDEVICE=nullpage", psPath);
        var raster = Path.Combine(Artifacts, "drawing-ps.png");
        await Run("-sDEVICE=png16m", "-r300", "-sOutputFile=" + raster, psPath);
        var reference = Path.Combine(Artifacts, "drawing-png.png");
        using (var png = File.Create(reference)) new PngRenderer(manager).RenderCanvas(finalized, page.Width, page.Height, png, 300);
        using var expected = SKBitmap.Decode(reference);
        using var actual = SKBitmap.Decode(raster);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var comparison = Compare(expected, actual, Path.Combine(Artifacts, "drawing-diff.png"));
        await File.WriteAllTextAsync(Path.Combine(Artifacts, "drawing-comparison.json"), JsonSerializer.Serialize(comparison));
        Assert.True(comparison.Passed, JsonSerializer.Serialize(comparison));

        foreach (var mutation in new[] { "missing-glyph", "displaced", "missing-line" })
        {
            var mutated = finalized.ToArray();
            if (mutation == "missing-line") mutated = mutated.Where(c => c is not DrawLineCommand).ToArray();
            else
            {
                var first = Assert.IsType<DrawTextCommand>(mutated[1]);
                mutated[1] = mutation == "displaced" ? first with { Bounds = first.Bounds with { X = first.Bounds.X + 5 } } :
                    first with { Text = "日本語 ABC 123", TextLayout = measurer.Layout("本語 ABC 123", first.Style.Font, first.Bounds.Width, false) };
            }
            var mutantPath = Path.Combine(Artifacts, mutation + ".ps");
            using (var output = File.Create(mutantPath)) new PostScriptRenderer(manager).Render(mutated, page, output);
            var mutantPng = Path.Combine(Artifacts, mutation + ".png");
            await Run("-sDEVICE=png16m", "-r300", "-sOutputFile=" + mutantPng, mutantPath);
            using var image = SKBitmap.Decode(mutantPng);
            var result = Compare(expected, image, Path.Combine(Artifacts, mutation + "-diff.png"));
            await File.WriteAllTextAsync(Path.Combine(Artifacts, mutation + "-comparison.json"), JsonSerializer.Serialize(result));
            Assert.False(result.Passed, mutation + " must be detected.");
        }
    }

    [GhostscriptFact]
    public async Task Multipage_document_executes_all_paper_sizes_and_records_measurements()
    {
        Directory.CreateDirectory(Artifacts);
        using var input = PostScriptTests.Workbook();
        using var output = new MemoryStream();
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        var timer = Stopwatch.StartNew();
        try
        {
            await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.PostScript, FontOptions = new() { AllowSystemFonts = false } }, new SingleStreamOutputSink(output));
        }
        finally { ConversionMetrics.Observer = null; }
        metrics["generation.ms"] = timer.Elapsed.TotalMilliseconds;
        metrics["processPeakWorkingSetBytes"] = Process.GetCurrentProcess().PeakWorkingSet64;
        var path = Path.Combine(Artifacts, "mixed-paper.ps");
        await File.WriteAllBytesAsync(path, output.ToArray());
        timer.Restart();
        await Run("-sDEVICE=nullpage", path);
        metrics["ghostscript.ms"] = timer.Elapsed.TotalMilliseconds;
        await Run("-sDEVICE=png16m", "-r72", "-sOutputFile=" + Path.Combine(Artifacts, "mixed-%02d.png"), path);
        using var a4 = SKBitmap.Decode(Path.Combine(Artifacts, "mixed-01.png"));
        using var a3 = SKBitmap.Decode(Path.Combine(Artifacts, "mixed-02.png"));
        Assert.Equal((595, 842), (a4.Width, a4.Height));
        Assert.Equal((1191, 842), (a3.Width, a3.Height));
        await File.WriteAllTextAsync(Path.Combine(Artifacts, "measurements.json"), JsonSerializer.Serialize(new { ghostscript = await Version(), repetitions = 1, pages = 2, metrics }));
    }

    [GhostscriptFact]
    public async Task Embedded_custom_collection_glyphs_render_after_source_deletion_without_platform_fonts()
    {
        Directory.CreateDirectory(Artifacts);
        var fontPath = Path.Combine(Artifacts, "temporary-font.ttc");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats/collection-distinct.ttc"), fontPath, true);
        using var resources = new RenderResourceSession();
        var manager = new FontManager(new() { AllowSystemFonts = false, Registrations = [new("Custom", fontPath) { RegularCollectionIndex = 1 }] });
        var text = PostScriptTests.Text("A B") with { Bounds = new(10, 10, 180, 80), Style = CellStyle.Default with { Font = new("Custom", 48) } };
        var commands = CommandTextFinalizer.Finalize([text], new SkiaTextMeasurer(manager), default).ToArray();
        var psPath = Path.Combine(Artifacts, "custom-collection.ps");
        var reference = Path.Combine(Artifacts, "custom-collection-png.png");
        using (var output = File.Create(psPath)) new PostScriptRenderer(manager).Render(commands, new(180, 144), output);
        using (var output = File.Create(reference)) new PngRenderer(manager).RenderCanvas(commands, 180, 144, output, 300);
        File.Delete(fontPath);
        await Run("-sDEVICE=nullpage", psPath);
        var raster = Path.Combine(Artifacts, "custom-collection-ps.png");
        await Run("-sDEVICE=png16m", "-r300", "-sOutputFile=" + raster, psPath);
        using var expected = SKBitmap.Decode(reference);
        using var actual = SKBitmap.Decode(raster);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var result = Compare(expected, actual, Path.Combine(Artifacts, "custom-collection-diff.png"));
        await File.WriteAllTextAsync(Path.Combine(Artifacts, "custom-collection-comparison.json"), JsonSerializer.Serialize(result));
        Assert.True(result.Passed, JsonSerializer.Serialize(result));
    }

    [GhostscriptFact]
    public async Task Representative_workloads_record_time_memory_size_and_shared_fonts()
    {
        Directory.CreateDirectory(Artifacts);
        var workloads = new Dictionary<string, DrawCommand[]>();
        var grid = new List<DrawCommand>();
        for (var row = 0; row < 20; row++)
        for (var column = 0; column < 8; column++)
        {
            var bounds = new Layout.ReportRect(10 + (column * 65), 10 + (row * 24), 65, 24);
            grid.Add(new DrawBorderCommand(1, bounds, new(new(), new(), new(), new())));
            grid.Add(PostScriptTests.Text("日本語 123") with { Bounds = bounds, Style = CellStyle.Default with { Font = new("Noto Sans JP", 9) } });
        }
        workloads["japanese-borders"] = grid.ToArray();
        workloads["repeated-text-10-pages"] = Enumerable.Range(1, 10).SelectMany(page => grid.Select(command => command with { PageNumber = page })).ToArray();
        workloads["images-4-pages"] = Enumerable.Range(1, 4).SelectMany(page => Fixture().Select(command => command with { PageNumber = page })).ToArray();
        workloads["many-glyphs"] = [PostScriptTests.Text(string.Concat(Enumerable.Range(0x4e00, 350).Select(char.ConvertFromUtf32))) with { Style = CellStyle.Default with { Font = new("Noto Sans JP", 10), WrapText = true } }];
        var results = new Dictionary<string, object>();
        var manager = PostScriptTests.Manager();
        foreach (var workload in workloads)
        {
            var samples = new List<object>();
            for (var repetition = 0; repetition < 3; repetition++)
            {
                var metrics = new Dictionary<string, double>();
                ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
                var timer = Stopwatch.StartNew();
                var file = Path.Combine(Artifacts, "performance-" + workload.Key + ".ps");
                try
                {
                    using var output = File.Create(file);
                    new PostScriptRenderer(manager).Render(workload.Value, new(595.28, 841.89), output);
                }
                finally { ConversionMetrics.Observer = null; }
                var generation = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                await Run("-sDEVICE=nullpage", file);
                samples.Add(new { generationMs = generation, ghostscriptMs = timer.Elapsed.TotalMilliseconds, psBytes = new FileInfo(file).Length, processPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64, metrics });
            }
            results[workload.Key] = new { pages = workload.Value.Select(command => command.PageNumber).Distinct().Count(), commands = workload.Value.Length, samples };
        }
        await File.WriteAllTextAsync(Path.Combine(Artifacts, "performance.json"), JsonSerializer.Serialize(new { runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processors = Environment.ProcessorCount, ghostscript = await Version(), repetitions = 3, results }));
    }

    [Theory]
    [InlineData("gray-removed", 180, 180, 180, 255, 255, 255)]
    [InlineData("dark-red-black", 120, 0, 0, 0, 0, 0)]
    [InlineData("equal-luminance", 120, 0, 0, 0, 36, 0)]
    [InlineData("gray-added", 255, 255, 255, 180, 180, 180)]
    [InlineData("near-white-removed", 250, 250, 250, 255, 255, 255)]
    public void Comparison_detects_local_color_and_coverage_changes(string name, byte r, byte g, byte b, byte mr, byte mg, byte mb)
    {
        Directory.CreateDirectory(Artifacts);
        using var expected = new SKBitmap(256, 128);
        using var actual = new SKBitmap(256, 128);
        expected.Erase(SKColors.White);
        actual.Erase(SKColors.White);
        // A local 16 x 16 change must not be diluted by the surrounding blank page.
        for (var y = 20; y < 36; y++)
        for (var x = 20; x < 36; x++)
        {
            expected.SetPixel(x, y, new(r, g, b));
            actual.SetPixel(x, y, new(mr, mg, mb));
        }
        SaveBitmap(expected, Path.Combine(Artifacts, name + "-synthetic-reference.png"));
        SaveBitmap(actual, Path.Combine(Artifacts, name + "-synthetic-actual.png"));
        var result = Compare(expected, actual, Path.Combine(Artifacts, name + "-synthetic-diff.png"));
        File.WriteAllText(Path.Combine(Artifacts, name + "-synthetic.json"), JsonSerializer.Serialize(result));
        Assert.False(result.Passed, JsonSerializer.Serialize(result));
        Assert.False(Compare(actual, expected, Path.Combine(Artifacts, name + "-synthetic-reverse-diff.png")).Passed);
    }

    [Theory]
    [InlineData(255, 255, true)]
    [InlineData(254, 255, true)]
    [InlineData(250, 250, true)]
    [InlineData(250, 255, false)]
    public void Comparison_handles_blank_and_low_contrast_pages(byte reference, byte candidate, bool passed)
    {
        Directory.CreateDirectory(Artifacts);
        using var expected = new SKBitmap(128, 128);
        using var actual = new SKBitmap(128, 128);
        expected.Erase(new SKColor(reference, reference, reference));
        actual.Erase(new SKColor(candidate, candidate, candidate));
        var result = Compare(expected, actual, Path.Combine(Artifacts, $"low-contrast-{reference}-{candidate}-diff.png"));
        Assert.Equal(passed, result.Passed);
    }

    [Fact]
    public void Comparison_detects_small_gray_loss_among_near_white_pixels()
    {
        Directory.CreateDirectory(Artifacts);
        using var expected = new SKBitmap(128, 128);
        using var actual = new SKBitmap(128, 128);
        expected.Erase(new SKColor(245, 245, 245));
        actual.Erase(new SKColor(245, 245, 245));
        for (var y = 20; y < 29; y++)
        for (var x = 20; x < 29; x++) expected.SetPixel(x, y, new(180, 180, 180));
        var result = Compare(expected, actual, Path.Combine(Artifacts, "local-gray-on-near-white-diff.png"));
        File.WriteAllText(Path.Combine(Artifacts, "local-gray-on-near-white.json"), JsonSerializer.Serialize(result));
        Assert.False(result.Passed, JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Comparison_accepts_small_boundary_rasterization_differences()
    {
        Directory.CreateDirectory(Artifacts);
        using var expected = new SKBitmap(128, 128);
        using var actual = new SKBitmap(128, 128);
        expected.Erase(SKColors.White);
        actual.Erase(SKColors.White);
        for (var y = 20; y < 80; y++)
        for (var x = 20; x < 80; x++)
        {
            expected.SetPixel(x, y, x == 20 ? new(100, 100, 100) : SKColors.Black);
            actual.SetPixel(x + 2, y, x == 20 ? new(160, 160, 160) : SKColors.Black);
        }
        var result = Compare(expected, actual, Path.Combine(Artifacts, "boundary-rasterization-diff.png"));
        Assert.True(result.Passed, JsonSerializer.Serialize(result));
    }

    [GhostscriptTheory]
    [InlineData("normal")]
    [InlineData("gray-removed")]
    [InlineData("dark-red-black")]
    [InlineData("gray-added")]
    public async Task Rasterized_gray_removal_and_dark_red_replacement_fail_the_normal_comparison(string mutation)
    {
        Directory.CreateDirectory(Artifacts);
        var manager = PostScriptTests.Manager();
        var page = new PageSettings(144, 144);
        DrawCommand[] commands =
        [
            new FillRectangleCommand(1, new(10, 10, 40, 30), new(180, 180, 180)),
            new DrawShapeCommand(1, new(70, 70, 40, 30), new(new(0, 0), 0, 0, 40, 30, ShapeKind.Ellipse, new(new(120, 0, 0), null), null, 0, 0)),
        ];
        var reference = Path.Combine(Artifacts, "review-colors-" + mutation + "-reference.png");
        using (var output = File.Create(reference)) new PngRenderer(manager).RenderCanvas(commands, page.Width, page.Height, output, 300);
        using var expected = SKBitmap.Decode(reference);
        var mutated = commands.ToArray();
        if (mutation == "gray-removed") mutated = [commands[1]];
        if (mutation == "dark-red-black")
        {
            var shape = (DrawShapeCommand)commands[1];
            mutated[1] = shape with { Shape = shape.Shape with { Style = shape.Shape.Style with { FillColor = new(0, 0, 0) } } };
        }
        if (mutation == "gray-added") mutated = [.. commands, new FillRectangleCommand(1, new(70, 10, 40, 30), new(180, 180, 180))];
        var path = Path.Combine(Artifacts, "review-colors-" + mutation);
        using (var output = File.Create(path + ".ps")) new PostScriptRenderer(manager).Render(mutated, page, output);
        await Run("-sDEVICE=nullpage", path + ".ps");
        await Run("-sDEVICE=png16m", "-r300", "-sOutputFile=" + path + ".png", path + ".ps");
        using var actual = SKBitmap.Decode(path + ".png");
        var result = Compare(expected, actual, path + "-diff.png");
        await File.WriteAllTextAsync(path + ".json", JsonSerializer.Serialize(result));
        Assert.Equal(mutation == "normal", result.Passed);
    }

    [GhostscriptTheory]
    [InlineData(0d)]
    [InlineData(1e-18)]
    [InlineData(0.5)]
    [InlineData(1d)]
    public async Task Zero_tiny_and_normal_width_lines_and_borders_execute_and_draw_dash_gaps(double width)
    {
        Directory.CreateDirectory(Artifacts);
        var styles = new[] { BorderLineStyle.Solid, BorderLineStyle.Dotted, BorderLineStyle.Dashed, BorderLineStyle.DashDot, BorderLineStyle.DashDotDot };
        var commands = new List<DrawCommand>();
        for (var i = 0; i < styles.Length; i++)
        {
            var side = new BorderSide(width, LineStyle: styles[i]);
            commands.Add(new DrawLineCommand(1, 10, 10 + (i * 25), 90, 10 + (i * 25), side));
            commands.Add(new DrawBorderCommand(1, new(105, 10 + (i * 25), 60, 15), new(side, side, side, side)));
        }
        var name = width == 0 ? "zero" : width < 1e-16 ? "tiny" : PostScriptWriter.Number(width);
        var path = Path.Combine(Artifacts, "review-dashes-" + name);
        using (var output = File.Create(path + ".ps")) new PostScriptRenderer(PostScriptTests.Manager()).Render(commands, new(180, 144), output);
        await Run("-sDEVICE=nullpage", path + ".ps");
        await Run("-sDEVICE=png16m", "-r300", "-sOutputFile=" + path + ".png", path + ".ps");
        using var raster = SKBitmap.Decode(path + ".png");
        for (var i = 0; i < styles.Length; i++)
        foreach (var border in new[] { false, true })
        {
            var y = (int)Math.Round((10 + (i * 25)) * 300d / 72);
            var first = (int)Math.Ceiling((border ? 108 : 13) * 300d / 72);
            var last = (int)Math.Floor((border ? 162 : 87) * 300d / 72);
            var ink = Enumerable.Range(first, last - first).Select(x => Enumerable.Range(y - 3, 7).Any(row => raster.GetPixel(x, row).Red < 245)).ToArray();
            Assert.Contains(true, ink);
            if (styles[i] == BorderLineStyle.Solid) Assert.All(ink, Assert.True);
            else Assert.Contains(false, ink);
        }
    }

    private static void SaveBitmap(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        encoded.SaveTo(stream);
    }

    private static DrawCommand[] Fixture()
    {
        using var bitmap = new SKBitmap(64, 64);
        bitmap.Erase(SKColors.Yellow);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            bitmap.SetPixel(x, y, SKColors.Red);
            bitmap.SetPixel(63 - x, 63 - y, SKColors.Blue);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return
        [
            new FillRectangleCommand(1, new(5, 5, 345, 385), new(245, 248, 250)),
            PostScriptTests.Text("日本語 ABC 123", y: 10),
            PostScriptTests.Text("辻\U000E0102 葛\U000E0102 \U0002000B", y: 40),
            PostScriptTests.Text("太字 斜体 下線", y: 70) with { Style = CellStyle.Default with { Font = new("Noto Sans JP", 16, Bold: true, Italic: true, Underline: true) } },
            PostScriptTests.Text("回転", y: 95) with { Bounds = new(10, 95, 70, 50), Style = CellStyle.Default with { Font = new("Noto Sans JP", 12), TextRotation = 25 } },
            new DrawLineCommand(1, 10, 160, 330, 160, new(1, new(20, 40, 80), BorderLineStyle.Dashed)),
            new DrawBorderCommand(1, new(10, 170, 100, 40), new(new(1, LineStyle: BorderLineStyle.Double), new(1, LineStyle: BorderLineStyle.DashDot), new(1, LineStyle: BorderLineStyle.Dotted), new(1, LineStyle: BorderLineStyle.DashDotDot))),
            new DrawImageCommand(1, new(130, 170, 60, 40), data.ToArray()) { Crop = new(.1, .1, .2, .2), FlipHorizontal = true, Rotation = 15 },
            new DrawShapeCommand(1, new(210, 170, 100, 40), new(new(0, 0), 0, 0, 100, 40, ShapeKind.RoundedRectangle, new(new(190, 210, 230), new(0, 0, 0)), new("図形", new("Noto Sans JP", 12), HorizontalAlignment.Center, VerticalAlignment.Center, false, 2, 2, 2, 2), 10, 0)),
            new DrawViewportCommand(1, [new DrawViewportCommand(1, [PostScriptTests.Text("クリップ ABC")], new(0, 0, 60, 25), 5, 3)], new(0, 0, 70, 35), 20, 230),
            new DrawShapeCommand(1, new(130, 230, 60, 35), new(new(0, 0), 0, 0, 60, 35, ShapeKind.Ellipse, new(new(120, 160, 200), new(0, 0, 0)), null, 0, 0)),
            new DrawShapeCommand(1, new(220, 230, 60, 35), new(new(0, 0), 0, 0, 60, 35, ShapeKind.WedgeRectangleCallout, new(new(200, 160, 120), new(0, 0, 0)), null, 0, 0)),
            PostScriptTests.Text("折返し日本語 ABC") with { Bounds = new(10, 300, 80, 60), Style = CellStyle.Default with { Font = new("Noto Sans JP", 16), WrapText = true } },
            PostScriptTests.Text("縮小文字 ABC 123") with { Bounds = new(120, 300, 80, 30), Style = CellStyle.Default with { Font = new("Noto Sans JP", 16), ShrinkToFit = true } },
            new DrawTextCommand(1, new(240, 300, 100, 50), "赤緑", CellStyle.Default with { Font = new("Noto Sans JP", 14) })
            {
                TextLayout = new(new(50, 20), [new("赤緑", 50, 20, 17,
                [
                    new(new("赤", OutputFixture.Face), 0, 12) { Font = new("Noto Sans JP", 9, Underline: true, Color: new(180, 0, 0)) },
                    new(new("緑", OutputFixture.Face), 20, 14) { Font = new("Noto Sans JP", 14, Color: new(0, 120, 0)) },
                ], false)]) { EffectiveFontSize = 14 },
            },
        ];
    }

    private static Comparison Compare(SKBitmap expected, SKBitmap actual, string diffPath)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        const int tile = 64;
        const int radius = 4;
        const int whiteTolerance = 2;
        const int interiorTolerance = 3;
        const int boundaryTolerance = 24;
        const double boundaryHueTolerance = .1;
        var columns = (expected.Width + tile - 1) / tile;
        var rows = (expected.Height + tile - 1) / tile;
        var contourInk = new int[columns * rows];
        var contourMissing = new int[contourInk.Length];
        var colorInk = new int[contourInk.Length];
        var colorMissing = new int[contourInk.Length];
        var regionInk = new int[contourInk.Length];
        var regionMissing = new int[contourInk.Length];
        var a = expected.Pixels;
        var b = actual.Pixels;
        using var diff = new SKBitmap(expected.Width, expected.Height);
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            var offset = (y * expected.Width) + x;
            diff.SetPixel(x, y, new((byte)Math.Abs(a[offset].Red - b[offset].Red), (byte)Math.Abs(a[offset].Green - b[offset].Green), (byte)Math.Abs(a[offset].Blue - b[offset].Blue)));
            Check(a, b, x, y);
            Check(b, a, x, y);
        }
        SaveBitmap(diff, diffPath);
        var contour = Measure(contourInk, contourMissing, relative: true);
        // Boundary discrepancies are relative to local content, including its solid interior.
        // Stable-region mismatches retain a separate absolute budget below.
        var color = Measure(colorInk.Zip(contourInk, Math.Max).ToArray(), colorMissing, relative: true);
        var region = Measure(regionInk, regionMissing, relative: false);
        return new(contour.FailedTiles.Length + color.FailedTiles.Length + region.FailedTiles.Length == 0, contour, color, region);

        static int Distance(SKColor left, SKColor right) => Math.Max(Math.Abs(left.Red - right.Red), Math.Max(Math.Abs(left.Green - right.Green), Math.Abs(left.Blue - right.Blue)));
        static int Contrast(SKColor color) => 255 - Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        static double HueDistance(SKColor left, SKColor right)
        {
            var a = (double)Contrast(left);
            var b = (double)Contrast(right);
            return Math.Max(Math.Abs(((255 - left.Red) / a) - ((255 - right.Red) / b)), Math.Max(Math.Abs(((255 - left.Green) / a) - ((255 - right.Green) / b)), Math.Abs(((255 - left.Blue) / a) - ((255 - right.Blue) / b))));
        }

        static ComparisonMetric Measure(int[] ink, int[] missing, bool relative) => new(ink.Sum(), missing.Sum(), missing.Max(), Enumerable.Range(0, ink.Length).Where(i => missing[i] > 15 && (!relative || missing[i] > ink[i] * .12)).ToArray());

        void Check(SKColor[] source, SKColor[] target, int x, int y)
        {
            var color = source[(y * expected.Width) + x];
            // Ignore only 1-2 channel levels next to white, not low-contrast content in general.
            if (Contrast(color) <= whiteTolerance) return;
            var index = (y / tile * columns) + (x / tile);
            var interior = true;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = Math.Clamp(x + dx, 0, expected.Width - 1);
                var sy = Math.Clamp(y + dy, 0, expected.Height - 1);
                if (Distance(color, source[(sy * expected.Width) + sx]) > whiteTolerance) interior = false;
            }

            // Geometry deliberately ignores color. Color checks below independently gate success.
            var contour = Contrast(color) > 20;
            if (contour) contourInk[index]++;
            if (interior) regionInk[index]++;
            else colorInk[index]++;
            var center = target[(y * expected.Width) + x];
            var geometryMatched = Contrast(center) > 20;
            var colorMatched = MatchesColor(center);
            if (!colorMatched || (contour && !geometryMatched))
            {
                for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (x + dx < 0 || y + dy < 0 || x + dx >= expected.Width || y + dy >= expected.Height) continue;
                    var candidate = target[((y + dy) * expected.Width) + x + dx];
                    geometryMatched |= Contrast(candidate) > 20;
                    colorMatched |= MatchesColor(candidate);
                }
            }
            if (contour && !geometryMatched) contourMissing[index]++;
            if (!colorMatched)
            {
                if (interior) regionMissing[index]++;
                else colorMissing[index]++;
            }

            bool MatchesColor(SKColor candidate) => Contrast(candidate) > whiteTolerance &&
                (Distance(color, candidate) <= (interior ? interiorTolerance : boundaryTolerance) ||
                (!interior && HueDistance(color, candidate) <= boundaryHueTolerance));
        }
    }

    private sealed record Comparison(bool Passed, ComparisonMetric Contour, ComparisonMetric Color, ComparisonMetric Region);

    private sealed record ComparisonMetric(int Compared, int Unmatched, int MaximumUnmatchedPerTile, int[] FailedTiles);

    internal static async Task<string> Run(params string[] arguments)
    {
        Assert.NotNull(Executable);
        var start = new ProcessStartInfo(Executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-dSAFER", "-dBATCH", "-dNOPAUSE", "-dNOPLATFONTS", "-dTextAlphaBits=4", "-dGraphicsAlphaBits=4" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var message = await stdout + await stderr;
        Directory.CreateDirectory(Artifacts);
        await File.WriteAllTextAsync(Path.Combine(Artifacts, $"ghostscript-{process.Id}.log"), message);
        ValidateResult(process.ExitCode, message, arguments.Contains("-sDEVICE=txtwrite"));
        return message;
    }

    internal static void ValidateResult(int exitCode, string message, bool textOutput)
    {
        Assert.True(exitCode == 0, message);
        var diagnostics = message;
        if (textOutput && message.StartsWith("GPL Ghostscript 10.02.1 (", StringComparison.Ordinal))
        {
            // Ubuntu 24.04's txtwrite emits this device cleanup diagnostic even for
            // a standalone Helvetica (ABC) showpage. Keep the raw log and reject
            // every other diagnostic, including another occurrence of this line.
            diagnostics = new Regex(@"(?m)^Error: finalizing subclassing device while child refcount > 1\r?$").Replace(diagnostics, "", 1);
        }

        Assert.False(Regex.IsMatch(diagnostics, "(?i)(substitut|error:|warning|can't find|cannot find)"), message);
    }

    [Fact]
    public void Ubuntu_txtwrite_cleanup_diagnostic_is_scoped_to_version_device_and_success()
    {
        const string banner = "GPL Ghostscript 10.02.1 (2023-11-01)\n";
        const string cleanup = "Error: finalizing subclassing device while child refcount > 1\n";
        ValidateResult(0, banner + cleanup, true);
        ValidateResult(0, banner + cleanup.Replace("\n", "\r\n", StringComparison.Ordinal), true);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateResult(1, banner + cleanup, true));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateResult(0, banner + cleanup, false));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateResult(0, banner.Replace("10.02.1", "10.05.1", StringComparison.Ordinal) + cleanup, true));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateResult(0, cleanup, true));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateResult(0, banner + cleanup + cleanup, true));
        foreach (var diagnostic in new[] { "Error: /invalidfont", "Error: /syntaxerror", "Warning: invalid font", "Substituting font", "Can't find font", "Cannot find font", cleanup.TrimEnd() + " unexpected" })
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateResult(0, banner + cleanup + diagnostic, true));
    }

    private static async Task<string> Version() => (await Run("--version")).Trim();

    private static string? FindExecutable()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "gswin64c.exe", "gswin32c.exe", "gs.exe" } : new[] { "gs" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

public sealed class GhostscriptFactAttribute : FactAttribute
{
    public GhostscriptFactAttribute()
    {
        if (PostScriptGhostscriptTests.Executable is null && Environment.GetEnvironmentVariable("EXCELRENDERER_REQUIRE_GHOSTSCRIPT") != "1")
            Skip = "Ghostscript is not installed; CI requires it with EXCELRENDERER_REQUIRE_GHOSTSCRIPT=1.";
    }
}

public sealed class GhostscriptTheoryAttribute : TheoryAttribute
{
    public GhostscriptTheoryAttribute()
    {
        if (PostScriptGhostscriptTests.Executable is null && Environment.GetEnvironmentVariable("EXCELRENDERER_REQUIRE_GHOSTSCRIPT") != "1")
            Skip = "Ghostscript is not installed; CI requires it with EXCELRENDERER_REQUIRE_GHOSTSCRIPT=1.";
    }
}
