using System.Text;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Drawing;
using ExcelRenderer.Pdf;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using ExcelRenderer.TestFixtures;
using SkiaSharp;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class PdfBackendTests
{
    [Fact]
    public void Defaults_select_skia()
    {
        Assert.Equal(PdfBackend.SkiaSharp, new PdfExportOptions().PdfBackend);
        Assert.Equal(PdfBackend.SkiaSharp, new RenderRequest().PdfBackend);
    }

    [Theory]
    [InlineData(null, Excel.FormulaDisplayMode.CachedValue, "7")]
    [InlineData(PdfBackend.SkiaSharp, Excel.FormulaDisplayMode.CachedValue, "7")]
    [InlineData(null, Excel.FormulaDisplayMode.Recalculate, "42")]
    [InlineData(PdfBackend.SkiaSharp, Excel.FormulaDisplayMode.Recalculate, "42")]
    public async Task File_api_propagates_reader_formula_policy(PdfBackend? backend, Excel.FormulaDisplayMode mode, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pdf-formula-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var texts = new List<string>();
        PdfRenderObserver.Current = (_, commands) => texts.AddRange(commands.OfType<DrawTextCommand>().Select(command => command.Text));
        try
        {
            using var input = SdkWorkbook.Create(new(new S.SheetData(new S.Row(new S.Cell(new S.CellFormula("20+22"), new S.CellValue("7")) { CellReference = "A1" }) { RowIndex = 1 })), SdkWorkbook.Styles());
            var path = Path.Combine(directory, "formula.xlsx");
            File.WriteAllBytes(path, input.ToArray());
            var output = Path.Combine(directory, "formula.pdf");
            await ExcelConverter.ConvertToPdfAsync(path, output, new()
            {
                PdfBackend = backend ?? new RenderRequest().PdfBackend,
                ReadOptions = new() { FormulaDisplayMode = mode },
                FontOptions = new() { AllowSystemFonts = false },
            });
            Assert.NotEmpty(texts);
            Assert.All(texts, text => Assert.Equal(expected, text));
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output)));
        }
        finally
        {
            PdfRenderObserver.Current = null;
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData((PdfBackend)42, OutputFormat.Pdf)]
    [InlineData((PdfBackend)42, OutputFormat.Png)]
    [InlineData((PdfBackend)42, OutputFormat.Svg)]
    [InlineData((PdfBackend)42, OutputFormat.Markdown)]
    public async Task Invalid_backend_is_rejected_before_reading_or_opening_output(PdfBackend? backend, OutputFormat format)
    {
        using var input = new UnreadableInput();
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.RenderAsync(input,
            new() { OutputFormat = format, PdfBackend = backend ?? new RenderRequest().PdfBackend }, new SingleStreamOutputSink(output)));
        Assert.Equal(0, output.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.ConvertToPdfAsync("missing.xlsx", "missing.pdf", new() { PdfBackend = (PdfBackend)42 }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PdfBackend.SkiaSharp)]
    public async Task Nonseekable_streams_and_borrowed_measurer_remain_open(PdfBackend? backend)
    {
        using var inputData = Workbook();
        using var input = new NonseekableStream(inputData);
        using var outputData = new MemoryStream();
        using var output = new NonseekableStream(outputData);
        var measurer = new BorrowedMeasurer();
        var result = await ExcelConverter.RenderAsync(input,
            new() { OutputFormat = OutputFormat.Pdf, PdfBackend = backend ?? new RenderRequest().PdfBackend, TextMeasurer = measurer, FontOptions = new() { AllowSystemFonts = false } },
            new SingleStreamOutputSink(output));
        Assert.Single(result.Pages);
        Assert.Single(result.Artifacts);
        Assert.True(measurer.Calls > 0);
        Assert.False(measurer.Disposed);
        Assert.True(inputData.CanRead);
        Assert.True(outputData.CanWrite);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(outputData.ToArray()));
        outputData.Position = 0;
        using var pdf = PdfTestFile.Open(outputData);
        Assert.Single(pdf.Pages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PdfBackend.SkiaSharp)]
    public async Task Cancelled_conversion_does_not_commit(PdfBackend? backend)
    {
        using var input = Workbook();
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.RenderAsync(input,
            new() { OutputFormat = OutputFormat.Pdf, PdfBackend = backend ?? new RenderRequest().PdfBackend }, new SingleStreamOutputSink(output), cancellation.Token));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Native_subset_retains_ivs_ids_and_removes_unused_outlines()
    {
        var root = FindRoot();
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, IvsFontStyle = IvsFontStyle.Mincho });
        var runs = manager.ResolveTextRuns("辻\U000E0102辻\U000E0103葛\U000E0102", new("IPAmjMincho", 400, false));
        Assert.Equal(new ushort?[] { 26795, 26796, 23477 }, runs.Select(run => run.GlyphId));
        var bytes = File.ReadAllBytes(Path.Combine(root, "third_party/IPAmjMincho/ipamjm.ttf"));
        var glyphs = runs.Select(run => run.GlyphId!.Value).Prepend((ushort)0).ToArray();
        var subset = Core.Pdf.HarfBuzzSubset.Create(bytes, glyphs, CancellationToken.None);
        Assert.True(subset.Length < bytes.Length / 10);
        using var sourceData = SKData.CreateCopy(bytes);
        using var subsetData = SKData.CreateCopy(subset);
        using var sourceFace = SKTypeface.FromData(sourceData);
        using var subsetFace = SKTypeface.FromData(subsetData);
        using var sourceFont = new SKFont(sourceFace, 12);
        using var subsetFont = new SKFont(subsetFace, 12);
        foreach (var glyph in glyphs)
        {
            using var originalPath = sourceFont.GetGlyphPath(glyph);
            using var subsetPath = subsetFont.GetGlyphPath(glyph);
            Assert.False(originalPath!.IsEmpty);
            Assert.Equal(originalPath!.ToSvgPathData(), subsetPath!.ToSvgPathData());
        }
    }

    [Fact]
    public void Collection_pass_omissions_fail_instead_of_writing_missing_glyphs()
    {
        using var bitmap = new SKBitmap(1, 1);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        using var fonts = new SkiaPdfFonts(CancellationToken.None);
        var face = OutputFixture.Face;
        fonts.Draw(canvas, new("A", face), 12, 0, 0, paint);
        fonts.Prepare();
        Assert.Throws<InvalidOperationException>(() => fonts.Draw(canvas, new("B", face), 12, 0, 0, paint));
    }

    [Fact]
    public void Equivalent_font_byte_snapshots_share_the_collected_subset()
    {
        using var bitmap = new SKBitmap(1, 1);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        using var fonts = new SkiaPdfFonts(CancellationToken.None);
        var face = OutputFixture.Face;
        fonts.Draw(canvas, new("A", face), 12, 0, 0, paint);
        fonts.Prepare();
        var equivalent = face with { FaceId = "new-record-same-physical-font", FontData = face.FontData!.ToArray() };
        fonts.Draw(canvas, new("A", equivalent), 12, 0, 0, paint);
    }

    [Theory]
    [InlineData("static.ttf")]
    [InlineData("cff.otf")]
    [InlineData("variable.ttf")]
    [InlineData("collection.ttc")]
    [InlineData("colr.ttf")]
    [InlineData("colr-v1.ttf")]
    public void Reachable_font_formats_keep_glyphs_and_subset_or_native_color_drawing(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", name));
        var face = new ResolvedFont("PDF Format Fixture", 400, false, string.Empty) { FontData = bytes, FaceId = name };
        var run = new TextRun("A B", face);
        using var surface = SKSurface.CreateNull(1, 1);
        using var resources = new SkiaPdfFonts(CancellationToken.None);
        using var paint = new SKPaint();
        resources.Draw(surface.Canvas, run, 20, 10, 30, paint);
        resources.Prepare();
        using var output = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(output))
        {
            var canvas = pdf.BeginPage(100, 100);
            resources.Draw(canvas, run, 20, 10, 30, paint);
            pdf.EndPage();
            pdf.Close();
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "PdfFormats");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), output.ToArray());
        output.Position = 0;
        using var parsed = PdfTestFile.Open(output);
        Assert.Single(parsed.Pages);
        Assert.True(output.Length > 500);
        if (!name.StartsWith("colr", StringComparison.Ordinal))
        {
            using var sourceData = SKData.CreateCopy(bytes);
            using var source = SKTypeface.FromData(sourceData);
            using var original = new SKFont(source, 20);
            var glyphs = original.GetGlyphs("AB");
            var subset = Core.Pdf.HarfBuzzSubset.Create(bytes, glyphs, CancellationToken.None);
            using var subsetData = SKData.CreateCopy(subset);
            using var subsetFace = SKTypeface.FromData(subsetData);
            using var subsetFont = new SKFont(subsetFace, 20);
            foreach (var glyph in glyphs)
            {
                using var before = original.GetGlyphPath(glyph);
                using var after = subsetFont.GetGlyphPath(glyph);
                Assert.Equal(before!.ToSvgPathData(), after!.ToSvgPathData());
            }
        }
    }

    [Theory]
    [InlineData("static.ttf")]
    [InlineData("cff.otf")]
    [InlineData("variable.ttf")]
    [InlineData("collection.ttc")]
    [InlineData("colr.ttf")]
    [InlineData("colr-v1.ttf")]
    public async Task Public_font_format_probe_loads_all_supported_snapshots(string name)
    {
        var outcomes = new List<object>();
        var successes = new List<bool>();
        foreach (var backend in new PdfBackend?[] { null, PdfBackend.SkiaSharp })
        {
            using var input = Workbook("A B");
            using var output = new MemoryStream();
            try
            {
                var result = await ExcelConverter.RenderAsync(input, new()
                {
                    OutputFormat = OutputFormat.Pdf,
                    PdfBackend = backend ?? new RenderRequest().PdfBackend,
                    FontOptions = new()
                    {
                        AllowSystemFonts = false,
                        UseFontPack = false,
                        FallbackFamilies = ["Noto Sans JP"],
                        Registrations = [new("Noto Sans JP", Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", name))],
                    },
                }, new SingleStreamOutputSink(output));
                successes.Add(true);
                Assert.Single(result.Artifacts);
                outcomes.Add(new { backend = backend.ToString(), success = true, error = (string?)null });
            }
            catch (ConversionException error)
            {
                successes.Add(false);
                Assert.Empty(error.CompletedArtifacts);
                outcomes.Add(new { backend = backend.ToString(), success = false, error = error.InnerException?.Message ?? error.Message });
            }
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "PdfFormats");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), System.Text.Json.JsonSerializer.Serialize(outcomes));
        Assert.All(successes, Assert.True);
    }

    [Fact]
    public async Task Default_and_explicit_skia_keep_page_plans_and_link_tooltips()
    {
        var results = new List<ConversionResult>();
        foreach (var backend in new PdfBackend?[] { null, PdfBackend.SkiaSharp })
        {
            using var input = Workbook(links: true);
            using var output = new MemoryStream();
            results.Add(await ExcelConverter.RenderAsync(input,
                new() { OutputFormat = OutputFormat.Pdf, PdfBackend = backend ?? new RenderRequest().PdfBackend, FontOptions = new() { AllowSystemFonts = false } }, new SingleStreamOutputSink(output)));
            output.Position = 0;
            using var pdf = PdfTestFile.Open(output);
            Assert.Single(pdf.Pages[0].Annotations);
            Assert.Equal("linktooltip", pdf.Pages[0].Annotations[0].Elements.GetString("/Contents"));
            Assert.InRange(pdf.Pages[0].MediaBox.Width, 595.27, 595.28);
            Assert.InRange(pdf.Pages[0].MediaBox.Height, 841.88, 841.90);
            Assert.True(output.Length < 1024 * 1024);
        }

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(results[0].Pages), System.Text.Json.JsonSerializer.Serialize(results[1].Pages));
        Assert.Equal(results[0].Diagnostics, results[1].Diagnostics);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(PdfBackend.SkiaSharp, false)]
    [InlineData(null, true)]
    [InlineData(PdfBackend.SkiaSharp, true)]
    public async Task Write_failure_or_midwrite_cancellation_aborts_once_and_releases_resources(PdfBackend? backend, bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var input = Workbook();
        using var output = new FailingOutput(cancel ? cancellation : null);
        var sink = new ObservedSink(output);
        var request = new RenderRequest { OutputFormat = OutputFormat.Pdf, PdfBackend = backend ?? new RenderRequest().PdfBackend, FontOptions = new() { AllowSystemFonts = false } };
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.RenderAsync(input, request, sink, cancellation.Token));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request, sink));
            Assert.Empty(failure.CompletedArtifacts);
        }

        Assert.Equal(1, sink.Aborts);
        Assert.Equal(0, sink.Completes);
        Assert.True(output.CanWrite);
        using var retryInput = Workbook();
        using var retryOutput = new MemoryStream();
        await ExcelConverter.RenderAsync(retryInput, request, new SingleStreamOutputSink(retryOutput));
        Assert.True(retryOutput.Length > 0);
    }

    [Fact]
    public async Task Four_concurrent_conversions_keep_fonts_and_resources_separate()
    {
        var tasks = Enumerable.Range(0, 4).Select(async index =>
        {
            using var input = Workbook(index % 2 == 0 ? "日本語 辻\U000E0102 😀" : "別書体 ABC 山⼭");
            using var output = new MemoryStream();
            var result = await ExcelConverter.RenderAsync(input, new()
            {
                OutputFormat = OutputFormat.Pdf,
                PdfBackend = index % 2 == 0 ? PdfBackend.SkiaSharp : new RenderRequest().PdfBackend,
                FontOptions = new()
                {
                    AllowSystemFonts = false,
                    IvsFontStyle = index % 2 == 0 ? IvsFontStyle.Mincho : IvsFontStyle.Gothic,
                    Registrations = index % 2 == 0 ? [] : [new("Noto Sans JP", Path.Combine(FindRoot(), "third_party/IPAmjMincho/ipamjm.ttf"))],
                },
            }, new SingleStreamOutputSink(output));
            Assert.Single(result.Pages);
            Assert.Single(result.Artifacts);
        });
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("preflight")]
    [InlineData("collect")]
    [InlineData("subset")]
    [InlineData("draw")]
    [InlineData("page-complete")]
    public async Task Cancellation_at_each_pdf_phase_releases_resources_and_aborts_once(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        using var input = Workbook("日本語 辻\U000E0102");
        using var output = new MemoryStream();
        var sink = new ObservedSink(output);
        var observed = false;
        var passes = 0;
        PdfRenderObserver.Current = (_, _) =>
        {
            passes++;
            if ((phase == "collect" && passes == 1) || (phase == "draw" && passes == 2))
            {
                observed = true;
                cancellation.Cancel();
            }
        };
        ConversionMetrics.Observer = (name, _) =>
        {
            if ((phase == "preflight" && name == "pageBuild.ms") || (phase == "subset" && name == "pdfSubset.ms") || (phase == "page-complete" && name == "pdfPage.ms"))
            {
                observed = true;
                cancellation.Cancel();
            }
        };
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.RenderAsync(input,
                new() { OutputFormat = OutputFormat.Pdf, PdfBackend = PdfBackend.SkiaSharp, FontOptions = new() { AllowSystemFonts = false } }, sink, cancellation.Token));
            Assert.True(observed, "Requested phase must actually execute.");
            Assert.Equal(phase == "preflight" ? 0 : 1, sink.Aborts);
            Assert.Equal(0, sink.Completes);
            Assert.True(output.CanWrite);
        }
        finally
        {
            ConversionMetrics.Observer = null;
            PdfRenderObserver.Current = null;
        }

        using var retryInput = Workbook();
        using var retryOutput = new MemoryStream();
        await ExcelConverter.RenderAsync(retryInput, new() { OutputFormat = OutputFormat.Pdf, PdfBackend = PdfBackend.SkiaSharp }, new SingleStreamOutputSink(retryOutput));
        Assert.True(retryOutput.Length > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Intermediate_spill_file_is_deleted_on_success_and_abort(bool abort)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pdf-spool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var writer = new SkiaPdfDocumentWriter(new FontManager(), CancellationToken.None,
                buffering: new() { MemoryThresholdBytes = 1, TemporaryDirectory = directory }))
            {
                writer.Prepare();
                writer.Append(new(100, 100), new(0, "spool", 1, 1, 1, 100, 100),
                    [new FillRectangleCommand(1, new(1, 1, 90, 90), new(20, 30, 40))], [], _ => { });
                if (!abort)
                {
                    using var output = new MemoryStream();
                    writer.Complete(output);
                    Assert.StartsWith("%PDF", Encoding.ASCII.GetString(output.ToArray()));
                }

                Assert.NotEmpty(Directory.GetFiles(directory));
            }

            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Pdf_image_interpolation_metadata_matches_existing_image_policy()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(new SKColor(200, 30, 40, 128));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var writer = new SkiaPdfDocumentWriter(new FontManager(), CancellationToken.None);
        writer.Prepare();
        writer.Append(new(100, 100), new(0, "image", 1, 1, 1, 100, 100),
            [new DrawImageCommand(1, new(10, 10, 80, 80), encoded.ToArray())], [], _ => { });
        using var output = new MemoryStream();
        writer.Complete(output);
        output.Position = 0;
        using var pdf = PdfTestFile.Open(output);
        var images = pdf.Internals.GetAllObjects().OfType<PdfTestMap>()
            .Where(dictionary => dictionary.Elements.GetName("/Subtype") == "/Image").ToArray();
        var color = Assert.Single(images, dictionary => dictionary.Elements.ContainsKey("/SMask"));
        Assert.True(color.Elements.GetBoolean("/Interpolate"));
        Assert.False(color.Elements.GetDictionary("/SMask")!.Elements.GetBoolean("/Interpolate"));
    }

    [Fact]
    public void Legacy_shape_and_vertical_text_are_finalized_before_the_writer()
    {
        var service = new SkiaSharp.SkiaTextMeasurer(new FontManager(new() { AllowSystemFonts = false }));
        DrawCommand[] commands =
        [
            new DrawTextCommand(1, new(10, 10, 80, 100), "日本", Model.CellStyle.Default with { TextRotation = 255 }),
            new DrawShapeCommand(1, new(10, 120, 150, 60), new(new(1, 1), 0, 0, 150, 60, Model.ShapeKind.Rectangle,
                new(null, null), new("図形 辻\U000E0102", new("Noto Sans JP", 12), Model.HorizontalAlignment.Center, Model.VerticalAlignment.Center, true, 2, 3, 4, 5), 0, 0)),
        ];
        var finalized = PdfCommandFinalizer.Finalize(commands, service, CancellationToken.None).ToArray();
        Assert.NotNull(Assert.IsType<DrawTextCommand>(finalized[0]).TextLayout);
        var shape = Assert.IsType<DrawShapeCommand>(finalized[1]);
        Assert.NotNull(shape.TextCommand!.TextLayout);
        Assert.Equal(new Layout.ReportRect(12, 123, 144, 52), shape.TextCommand.Bounds);
        Assert.Contains(shape.TextCommand.TextLayout!.Lines.SelectMany(line => line.Runs), run => run.Run.GlyphId is not null);
        var repeated = PdfCommandFinalizer.Finalize([finalized[0]], service, CancellationToken.None).Single();
        Assert.Same(finalized[0], repeated);
    }

    [Fact]
    public void Legacy_measured_lines_gain_font_runs_without_changing_caller_geometry()
    {
        var command = OutputFixture.Artificial(runs: false);
        var layout = command.TextLayout!;
        var service = new SkiaSharp.SkiaTextMeasurer(new FontManager(new() { AllowSystemFonts = false }));
        var finalized = Assert.IsType<DrawTextCommand>(PdfCommandFinalizer.Finalize([command], service, CancellationToken.None).Single());
        Assert.Equal(layout.Size, finalized.TextLayout!.Size);
        Assert.Equal(layout.EffectiveFontSize, finalized.TextLayout.EffectiveFontSize);
        foreach (var pair in layout.Lines.Zip(finalized.TextLayout.Lines))
        {
            Assert.Equal(pair.First.Width, pair.Second.Width);
            Assert.Equal(pair.First.Height, pair.Second.Height);
            Assert.Equal(pair.First.Baseline, pair.Second.Baseline);
            Assert.NotEmpty(pair.Second.Runs);
            Assert.Equal(pair.First.Text, string.Concat(pair.Second.Runs.Select(run => run.Run.SourceText)));
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(PdfBackend.SkiaSharp, false)]
    [InlineData(null, true)]
    [InlineData(PdfBackend.SkiaSharp, true)]
    public async Task Corrupt_images_keep_warning_locations_and_strict_failure_policy(PdfBackend? backend, bool strict)
    {
        using var input = Workbook();
        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
        {
            SdkWorkbook.Image(document.WorkbookPart!.WorksheetParts.Single(), [1, 2, 3, 4]);
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var sink = new ObservedSink(output);
        var request = new RenderRequest
        {
            OutputFormat = OutputFormat.Pdf,
            PdfBackend = backend ?? new RenderRequest().PdfBackend,
            DiagnosticOptions = new() { StrictMode = strict },
            FontOptions = new() { AllowSystemFonts = false },
        };
        IReadOnlyList<ConversionDiagnostic> diagnostics;
        if (strict)
        {
            var failure = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request, sink));
            diagnostics = failure.CollectedDiagnostics;
            Assert.Empty(failure.CompletedArtifacts);
            Assert.Equal(1, sink.Aborts);
            Assert.Equal(0, sink.Completes);
        }
        else
        {
            var result = await ExcelConverter.RenderAsync(input, request, sink);
            diagnostics = result.Diagnostics;
            Assert.Equal(1, sink.Completes);
        }

        var warning = Assert.Single(diagnostics, diagnostic => diagnostic.Code == "ImageDecodeFailed");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(DiagnosticStage.Render, warning.Stage);
        Assert.Equal("data", warning.SheetName);
        Assert.Equal(1, warning.SourcePageNumber);
        Assert.StartsWith("image@", warning.ObjectId);
    }

    private static MemoryStream Workbook(string text = "日本語 ABC", bool links = false)
    {
        var sheet = new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", text)) { RowIndex = 1 }), new S.PageSetup { PaperSize = 9 });
        if (links)
        {
            sheet.Append(new S.Hyperlinks(new S.Hyperlink { Reference = "A1", Location = "A1", Tooltip = "link\ntooltip" }));
        }

        return SdkWorkbook.Create(sheet, SdkWorkbook.Styles());
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExcelRenderer.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository fixture root not found.");
    }

    private sealed class FailingOutput(CancellationTokenSource? cancellation) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (cancellation is null) throw new IOException("Injected PDF write failure.");
            base.Write(buffer, offset, count);
            cancellation.Cancel();
        }
    }

    private sealed class ObservedSink(Stream output) : IRenderOutputSink
    {
        public int Aborts { get; private set; }
        public int Completes { get; private set; }
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(output);
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
        {
            Completes++;
            return default;
        }

        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken)
        {
            Aborts++;
            return default;
        }
    }

    private sealed class BorrowedMeasurer : ITextMeasurer, IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public TextSize Measure(string text, Model.FontStyle font, double availableWidth, bool wrap)
        {
            Calls++;
            return new(text.Length * 10, 15);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class UnreadableInput : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Input must not be read.");
    }

    private sealed class NonseekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
