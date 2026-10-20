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
            Assert.False(Compare(expected, image, Path.Combine(Artifacts, mutation + "-diff.png")).Passed, mutation + " must be detected.");
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
        const int tile = 64;
        var columns = (expected.Width + tile - 1) / tile;
        var rows = (expected.Height + tile - 1) / tile;
        var ink = new int[columns * rows];
        var unmatched = new int[ink.Length];
        using var diff = new SKBitmap(expected.Width, expected.Height);
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            var a = expected.GetPixel(x, y);
            var b = actual.GetPixel(x, y);
            diff.SetPixel(x, y, new((byte)Math.Abs(a.Red - b.Red), (byte)Math.Abs(a.Green - b.Green), (byte)Math.Abs(a.Blue - b.Blue)));
            Check(expected, actual, x, y);
            Check(actual, expected, x, y);
        }
        using (var image = SKImage.FromBitmap(diff))
        using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(diffPath)) encoded.SaveTo(stream);
        var failed = Enumerable.Range(0, ink.Length).Where(i => unmatched[i] > 15 && unmatched[i] > ink[i] * .12).ToArray();
        return new(failed.Length == 0, unmatched.Sum(), ink.Sum(), failed);

        void Check(SKBitmap source, SKBitmap target, int x, int y)
        {
            var color = source.GetPixel(x, y);
            // Compare colored content as well as text against a small neighborhood for antialiasing differences.
            if (color.Red > 235 && color.Green > 235 && color.Blue > 235) return;
            var dark = Math.Max(color.Red, Math.Max(color.Green, color.Blue)) < 150;
            if (!dark && Math.Max(color.Red, Math.Max(color.Green, color.Blue)) - Math.Min(color.Red, Math.Min(color.Green, color.Blue)) < 35) return;
            var index = (y / tile * columns) + (x / tile);
            ink[index]++;
            for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                if (x + dx < 0 || y + dy < 0 || x + dx >= target.Width || y + dy >= target.Height) continue;
                var candidate = target.GetPixel(x + dx, y + dy);
                if (dark && Math.Max(candidate.Red, Math.Max(candidate.Green, candidate.Blue)) < 150) return;
                if (Math.Abs(color.Red - candidate.Red) + Math.Abs(color.Green - candidate.Green) + Math.Abs(color.Blue - candidate.Blue) < 100) return;
            }
            unmatched[index]++;
        }
    }

    private sealed record Comparison(bool Passed, int Unmatched, int Ink, int[] FailedTiles);

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
