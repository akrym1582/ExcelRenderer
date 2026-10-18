using System.Buffers.Binary;
using System.Text.Json;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class PdfRemovalContractTests
{
    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    [InlineData(OutputFormat.Markdown)]
    public async Task Reserved_zero_fails_before_input_and_sink_access(OutputFormat format)
    {
        using var input = new MemoryStream([1, 2, 3]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => ExcelConverter.RenderAsync(input,
            new() { OutputFormat = format, PdfBackend = (PdfBackend)0 }, new SingleStreamOutputSink(output)));
        Assert.Equal(0, input.Position);
        Assert.Equal(0, output.Length);
        await Assert.ThrowsAsync<NotSupportedException>(() => ExcelConverter.ConvertToPdfAsync("missing.xlsx", "missing.pdf", new() { PdfBackend = (PdfBackend)0 }));
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    [InlineData(OutputFormat.Markdown)]
    public async Task Default_engine_allows_every_output_format(OutputFormat format)
    {
        using var input = Workbook();
        using var output = new MemoryStream();
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (key, value) => metrics[key] = metrics.GetValueOrDefault(key) + value;
        try
        {
            var result = await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, FontOptions = new() { AllowSystemFonts = false } }, new SingleStreamOutputSink(output));
            Assert.Single(result.Artifacts);
            Assert.True(output.Length > 0);
            if (format == OutputFormat.Markdown)
            {
                Assert.False(metrics.ContainsKey("pdfBackend"));
                Assert.False(metrics.ContainsKey("pdfSubset.ms"));
                Assert.False(metrics.ContainsKey("pdfSave"));
            }
        }
        finally { ConversionMetrics.Observer = null; }
    }

    [Fact]
    public void Serialized_engine_keeps_numbers_and_omission_default()
    {
        Assert.Equal(PdfBackend.SkiaSharp, JsonSerializer.Deserialize<RenderRequest>("{}")!.PdfBackend);
        Assert.Equal(PdfBackend.SkiaSharp, JsonSerializer.Deserialize<PdfExportOptions>("{}")!.PdfBackend);
        Assert.Equal((PdfBackend)0, JsonSerializer.Deserialize<RenderRequest>("{\"PdfBackend\":0}")!.PdfBackend);
        Assert.Equal(PdfBackend.SkiaSharp, JsonSerializer.Deserialize<RenderRequest>("{\"PdfBackend\":1}")!.PdfBackend);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RenderRequest>("{\"PdfBackend\":null}"));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(10.125)]
    public void Advances_and_wrap_boundaries_match_design_units(double size)
    {
        using var resources = new ConversionFontResources();
        var manager = new OutputFixture.FixedManager();
        var face = resources.GetFace(OutputFixture.Face);
        var measurer = new SkiaTextMeasurer(manager);
        var font = new FontStyle("Noto Sans JP", size);
        foreach (var text in new[] { "ABC xyz 0123456789 ", "日本語 帳票 ", "AVfi", "e\u0301", "𠮷", new string('A', 1000) })
        {
            var layout = measurer.Layout(text, font, 10000, false);
            var expected = face.Measure(face.Typeface.GetGlyphs(text), size);
            Assert.InRange(Math.Abs(layout.Size.Width - expected), 0, text.Length == 1000 ? .05 : .01);
            Assert.All(layout.Lines.SelectMany(line => line.Runs), run => Assert.NotNull(run.Glyphs));
            foreach (var delta in new[] { -.01, -.001, 0, .001, .01 })
            {
                var wrapped = measurer.Layout("AA", font, face.Measure(face.Typeface.GetGlyphs("AA"), size) + delta, true);
                Assert.Equal(delta < 0 ? 2 : 1, wrapped.Lines.Count);
                Assert.Equal("AA", string.Concat(wrapped.Lines.Select(line => line.Text)));
            }
        }
    }

    [Fact]
    public void Frozen_metrics_match_all_36_pre_removal_samples()
    {
        var root = FindRoot();
        using var samples = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/Shared/PdfRemovalBaseline/metrics.json")));
        using var face = global::SkiaSharp.SKTypeface.FromFile(Path.Combine(root, "third_party/NotoSansJP/NotoSansJP-Regular.ttf"));
        var head = face.GetTableData(0x68656164);
        var hhea = face.GetTableData(0x68686561);
        var units = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(18));
        var ascent = BinaryPrimitives.ReadInt16BigEndian(hhea.AsSpan(4));
        var observations = new List<object>();
        var manager = new FontManager(new() { AllowSystemFonts = false });
        var measurer = new SkiaTextMeasurer(manager);
        foreach (var item in samples.RootElement.EnumerateArray())
        {
            var layout = measurer.Layout(item.GetProperty("text").GetString()!, new("Noto Sans JP", item.GetProperty("size").GetDouble()), 10000, false);
            Assert.InRange(Math.Abs(layout.Size.Width - item.GetProperty("Size").GetProperty("Width").GetDouble()), 0, .0000001);
            Assert.Equal(item.GetProperty("lines").GetArrayLength(), layout.Lines.Count);
            foreach (var (line, expected) in layout.Lines.Zip(item.GetProperty("lines").EnumerateArray()))
            {
                Assert.Equal(expected.GetProperty("Text").GetString(), line.Text);
                var size = (float)item.GetProperty("size").GetDouble();
                var frozenBaseline = expected.GetProperty("Baseline").GetDouble();
                // FreeType normalizes design metrics before scaling. DirectWrite scales
                // before division (SkScalerContext_win_dw.cpp, generateFontMetrics).
                // Assert each exact arithmetic path; retain the original numeric threshold.
                Assert.Equal(frozenBaseline, (double)(ascent / (float)units * size));
                var platformBaseline = OperatingSystem.IsWindows() ? (double)(size * ascent / units) : frozenBaseline;
                observations.Add(new { Size = size, Frozen = frozenBaseline, Expected = platformBaseline, Actual = line.Baseline });
                Assert.InRange(Math.Abs(line.Baseline - platformBaseline), 0, .0000001);
            }
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs/PdfMetrics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "platform-metrics.json"), JsonSerializer.Serialize(observations));
    }

    [Fact]
    public void Layout_replays_after_scope_disposal_and_font_file_removal()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ttf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"), path);
        TextLayoutResult layout;
        try
        {
            var manager = new FontManager(new() { UseFontPack = false, AllowSystemFonts = false, Registrations = [new("Noto Sans JP", path)] });
            layout = new SkiaTextMeasurer(manager).Layout("ABC 日本語", new("Noto Sans JP", 12), 300, false);
        }
        finally { File.Delete(path); }
        Assert.Null(ConversionFontResources.Current);
        using var output = new MemoryStream();
        new PdfRenderer(new NoResolutionManager()).Render([new DrawTextCommand(1, new(10, 10, 300, 40), "ABC 日本語", CellStyle.Default) { TextLayout = layout }], new(400, 100), output);
        using var pdf = PdfTestFile.Open(new MemoryStream(output.ToArray()));
        Assert.Contains("ABC", pdf.Pages[0].Text);
        Assert.Contains("本語", pdf.Pages[0].Text);
        Assert.True(pdf.Pages[0].Text.Contains("日本語", StringComparison.Ordinal) || pdf.Pages[0].ActualTexts.Any(value => value.Contains("日", StringComparison.Ordinal)));
        Assert.Equal("ABC 日本語", string.Concat(layout.Lines.SelectMany(line => line.Runs).Select(run => run.Run.SourceText)));
    }

    [Fact]
    public void Finalized_layout_snapshot_does_not_expose_or_borrow_mutable_caller_font_bytes()
    {
        var face = OutputFixture.Face;
        var layout = new SkiaTextMeasurer(new SnapshotManager(face)).Layout("ABC 日本語", new("Noto Sans JP", 12), 300, false);
        Assert.All(layout.Lines.SelectMany(line => line.Runs), run => Assert.Same(face.FontData, run.Run.Font.FontData));
        Array.Clear(face.FontData!);
        using var output = new MemoryStream();
        new PdfRenderer(new NoResolutionManager()).Render([new DrawTextCommand(1, new(10, 10, 300, 40), "ABC 日本語", CellStyle.Default) { TextLayout = layout }], new(400, 100), output);
        using var pdf = PdfTestFile.Open(new MemoryStream(output.ToArray()));
        Assert.Contains("ABC", pdf.Pages[0].Text);
        Assert.Contains("本語", pdf.Pages[0].Text);
    }

    [Fact]
    public void Standalone_rich_layout_resolves_a_changed_custom_physical_face()
    {
        var manager = new SnapshotManager(OutputFixture.Face);
        var service = new SkiaTextMeasurer(manager);
        var font = new FontStyle("Noto Sans JP", 12);
        CellTextRun[] runs = [new("WWW iii 123", font)];
        var first = service.Layout(runs, font, 300, false);
        manager.Font = manager.Font with { FontData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts/NotoSansMono-Regular.ttf")) };
        var second = service.Layout(runs, font, 300, false);
        Assert.NotEqual(first.Size.Width, second.Size.Width);
        Assert.Null(ConversionFontResources.Current);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task Same_family_different_snapshots_and_mixed_products_remain_isolated(int parallelism)
    {
        await Parallel.ForEachAsync(Enumerable.Range(0, 24), new ParallelOptions { MaxDegreeOfParallelism = parallelism }, async (index, _) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, index % 2 == 0 ? "NotoSansJP-Regular.ttf" : "Fonts/NotoSansMono-Regular.ttf");
            var counters = new Dictionary<string, double>();
            ConversionMetrics.Observer = (key, value) => counters[key] = counters.GetValueOrDefault(key) + value;
            try
            {
                using var input = Workbook();
                using var output = new MemoryStream();
                if (index % 3 == 0)
                    await Core.ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = path });
                else
                    await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Pdf, FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, Registrations = [new("Noto Sans JP", path)] } }, new SingleStreamOutputSink(output));
                using var pdf = PdfTestFile.Open(new MemoryStream(output.ToArray()));
                Assert.Contains("ABC", pdf.Pages[0].Text);
                Assert.Equal(counters["typefaceCreated"], counters["typefaceDisposed"]);
                Assert.Equal(counters["pdfSubsetTypefaceCreated"], counters["pdfSubsetTypefaceDisposed"]);
                Assert.Equal(counters["pdfFontCreated"], counters["pdfFontDisposed"]);
                Assert.Null(ConversionFontResources.Current);
            }
            finally { ConversionMetrics.Observer = null; }
        });
    }

    [Fact]
    public async Task Public_standalone_measurement_owns_scopes_and_can_share_its_service()
    {
        using var outer = new ConversionFontResources();
        var outerFace = outer.GetFace(OutputFixture.Face);
        var measurer = new SkiaTextMeasurer(new OutputFixture.FixedManager());
        await Parallel.ForEachAsync(Enumerable.Range(0, 16), new ParallelOptions { MaxDegreeOfParallelism = 8 }, (index, _) =>
        {
            var created = 0d; var disposed = 0d;
            ConversionMetrics.Observer = (name, value) =>
            {
                if (name == "typefaceCreated") { created += value; Assert.NotSame(outer, ConversionFontResources.Current); }
                if (name == "typefaceDisposed") disposed += value;
            };
            try
            {
                var value = measurer.Layout("ABC " + index, new("Noto Sans JP", 12), 100, false);
                Assert.True(value.Size.Width > 0);
                Assert.Equal(1, created);
                Assert.Equal(created, disposed);
                Assert.Same(outer, ConversionFontResources.Current);
            }
            finally { ConversionMetrics.Observer = null; }
            return ValueTask.CompletedTask;
        });
        Assert.Same(outerFace, outer.GetFace(OutputFixture.Face));
        Assert.NotEqual(IntPtr.Zero, outerFace.Typeface.Handle);
    }

    [Fact]
    public async Task Independent_renderers_own_and_restore_scopes_when_parallel_calls_inherit_an_outer_session()
    {
        using var outer = new RenderResourceSession();
        await Parallel.ForEachAsync(Enumerable.Range(0, 8), new ParallelOptions { MaxDegreeOfParallelism = 8 }, (index, _) =>
        {
            var created = 0d;
            var disposed = 0d;
            ConversionMetrics.Observer = (name, value) =>
            {
                if (name == "typefaceCreated") { created += value; Assert.NotSame(outer, RenderResourceSession.Current); }
                if (name == "typefaceDisposed") disposed += value;
            };
            try
            {
                using var output = new MemoryStream();
                var command = new DrawTextCommand(1, new(10, 10, 80, 30), "ABC", CellStyle.Default);
                new PdfRenderer(new OutputFixture.FixedManager()).Render([command], OutputFixture.Page, output);
                Assert.True(output.Length > 0);
                Assert.True(created > 0);
                Assert.Equal(created, disposed);
                Assert.Same(outer, RenderResourceSession.Current);
            }
            finally { ConversionMetrics.Observer = null; }
            return ValueTask.CompletedTask;
        });
    }

    [Fact]
    public async Task Caller_supplied_skia_service_borrows_its_conversion_scope_without_owning_the_service()
    {
        var service = new SkiaTextMeasurer(new SnapshotManager(OutputFixture.Face));
        var created = 0d;
        var disposed = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "typefaceCreated") created += value;
            if (name == "typefaceDisposed") disposed += value;
        };
        try
        {
            using var input = SdkWorkbook.Create(new(new S.SheetData(Enumerable.Range(1, 12).Select(index => new S.Row(SdkWorkbook.Text("A" + index, "ABC " + index)) { RowIndex = (uint)index }))), SdkWorkbook.Styles());
            using var output = new MemoryStream();
            await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Pdf, TextMeasurer = service, FontOptions = new() { AllowSystemFonts = false } }, new SingleStreamOutputSink(output));
            Assert.InRange(created, 1, 2);
            Assert.Equal(created, disposed);
            Assert.Null(RenderResourceSession.Current);
            Assert.True(service.Measure("ABC", new("Noto Sans JP", 12), 300, false).Width > 0);
            Assert.Equal(created, disposed);
        }
        finally { ConversionMetrics.Observer = null; }
    }

    [Fact]
    public async Task Loaded_product_assemblies_exclude_the_removed_engine_after_real_conversions()
    {
        using var input = Workbook();
        using var mapped = new MemoryStream();
        Mapping.ExcelTemplateMapper.Map(input, mapped, new { title = "ABC" });
        mapped.Position = 0;
        using var core = new MemoryStream();
        await Core.ExcelConverter.ConvertAsync(mapped, core, new() { FontFilePath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf") });
        mapped.Position = 0;
        using var full = new MemoryStream();
        await ExcelConverter.RenderAsync(mapped, new() { OutputFormat = OutputFormat.Pdf, FontOptions = new() { AllowSystemFonts = false } }, new SingleStreamOutputSink(full));
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name!).Order().ToArray();
        Assert.DoesNotContain(loaded, name => name.Contains("PdfSharp", StringComparison.OrdinalIgnoreCase));
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "PdfRemoval");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "loaded-assemblies.json"), JsonSerializer.Serialize(new { rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, loaded }));
    }

    private static MemoryStream Workbook() => SdkWorkbook.Create(new(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "ABC 123")) { RowIndex = 1 })), SdkWorkbook.Styles());
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "ExcelRenderer.slnx"))) directory = directory.Parent ?? throw new DirectoryNotFoundException();
        return directory.FullName;
    }
    private sealed class SnapshotManager(ResolvedFont font) : IFontManager
    {
        internal ResolvedFont Font { get; set; } = font;
        public ResolvedFont Resolve(FontRequest request) => Font;
        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request) => [new(text, Font)];
    }

    private sealed class NoResolutionManager : IFontManager
    {
        public ResolvedFont Resolve(FontRequest request) => throw new InvalidOperationException("Finalized layout must not resolve fonts again");
        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request) => throw new InvalidOperationException("Finalized layout must not shape or resolve text again");
    }
}
