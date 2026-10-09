using ClosedXML.Excel;
using ExcelRenderer.Core.Rendering;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class CoreIntegrationTests
{
    [Fact]
    public async Task Reader_hooks_share_traversal_and_preserve_product_style_policies()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("excluded").Cell(1, 1).Value = "not selected";
        var source = workbook.AddWorksheet("selected").Cell(1, 1);
        source.Value = "漢\uFE00";
        source.Style.Font.FontName = "Original family";
        source.Style.Font.Bold = true;
        source.Style.Font.Italic = true;
        source.Style.Alignment.TextRotation = 30;
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        using var prepared = await Core.Input.WorkbookInputPreparer.ReadAsync(input, new Core.WorkbookInputOptions(), CancellationToken.None);
        using var font = new Core.Fonts.SingleFontContext(CoreOptions().FontFilePath);
        var reads = 0;
        Core.Rendering.ConversionMetrics.Observer = (name, value) => { if (name == "coreReaderWorkbook") reads += (int)value; };
        try
        {
            var basic = new Core.Excel.CoreExcelReader(font).Read(prepared, null, ["selected"]);
            var full = new CoreIntegration.FullExcelReader(null, null, new()).Read(prepared, null, ["selected"]);
            Assert.Equal(2, reads);
            var basicSheet = Assert.Single(basic.Sheets);
            Assert.Equal(2, basicSheet.SourceSheetIndex);
            var basicCell = Assert.Single(basicSheet.Cells).Value;
            Assert.Equal("漢", basicCell.Text);
            Assert.False(basicCell.Style.Font.Bold);
            Assert.False(basicCell.Style.Font.Italic);
            Assert.Equal(0, basicCell.Style.TextRotation);
            Assert.Empty(full.Sheets[0].Cells);
            Assert.Equal(2, full.Sheets[1].SourceSheetIndex);
            var fullCell = Assert.Single(full.Sheets[1].Cells).Value;
            Assert.Equal("漢\uFE00", fullCell.Text);
            Assert.Equal("Original family", fullCell.Style.Font.Family);
            Assert.True(fullCell.Style.Font.Bold);
            Assert.True(fullCell.Style.Font.Italic);
            Assert.Equal(30, fullCell.Style.TextRotation);
        }
        finally
        {
            Core.Rendering.ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public async Task Core_and_full_conversions_share_font_state_and_recover_after_failure()
    {
        var bytes = Workbook();
        await ConvertCore(bytes);
        await ConvertFull(bytes);
        await ConvertCore(bytes);
        using (var input = new MemoryStream(bytes))
        using (var output = new FailingOutput())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Core.ExcelConverter.ConvertAsync(input, output, CoreOptions()));
        }

        await ConvertFull(bytes);
        await ConvertCore(bytes);
        Assert.Null(GlobalFontSettings.FontResolver);
    }

    [Fact]
    public async Task Parallel_mixed_conversions_release_native_fonts_and_keep_observers_local()
    {
        var bytes = Workbook();
        var jobs = Enumerable.Range(0, 50).Select(index => Task.Run(async () =>
        {
            var created = 0d;
            var disposed = 0d;
            Core.Rendering.ConversionMetrics.Observer = (name, value) =>
            {
                if (name == "typefaceCreated") created += value;
                if (name == "typefaceDisposed") disposed += value;
            };
            try
            {
                if (index % 2 == 0)
                {
                    await ConvertCore(bytes);
                    Assert.Equal(1, created);
                    Assert.Equal(created, disposed);
                }
                else
                {
                    await ConvertFull(bytes);
                    Assert.True(created > 0);
                    Assert.Equal(created, disposed);
                }
            }
            finally
            {
                Core.Rendering.ConversionMetrics.Observer = null;
            }
        }));
        await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(90));
        await ConvertCore(bytes);
    }

    [Fact]
    public async Task Cancellation_while_waiting_does_not_release_another_font_session()
    {
        var bytes = Workbook();
        using var cancellation = new CancellationTokenSource();
        using (var session = PdfSharpFontGate.Acquire())
        {
            var pending = PdfSharpFontGate.AcquireAsync(cancellation.Token);
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            using var secondCancellation = new CancellationTokenSource();
            secondCancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => PdfSharpFontGate.Acquire(secondCancellation.Token));
        }

        await ConvertFull(bytes);
        await ConvertCore(bytes);
    }

    [Fact]
    public async Task Public_finalized_layout_can_be_rendered_after_a_core_conversion()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false });
        var measurer = new PdfSharp.PdfSharpTextMeasurer(manager);
        var style = Model.CellStyle.Default;
        var layout = measurer.Layout("日本語 ABC", style.Font, 300, false);
        await ConvertCore(Workbook());
        using var output = new MemoryStream();
        new PdfSharp.PdfSharpRenderer(manager).Render(
            [new Drawing.DrawTextCommand(1, new(20, 20, 300, 60), "日本語 ABC", style) { TextLayout = layout }],
            new Model.PageSettings(),
            output);
        output.Position = 0;
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, pdf.PageCount);
    }

    [Fact]
    public async Task Both_products_use_shared_geometry_page_builder_commands_and_border_strokes()
    {
        foreach (var core in new[] { true, false })
        {
            var metrics = new Dictionary<string, double>();
            Core.Rendering.ConversionMetrics.Observer = (name, value) => metrics[name] = Math.Max(metrics.GetValueOrDefault(name), value);
            try
            {
                if (core) await ConvertCore(Workbook());
                else await ConvertFull(Workbook());
                Assert.Equal(1, metrics["coreGeometryPlan"]);
                Assert.Equal(1, metrics["corePageBuild"]);
                Assert.Equal(1, metrics["coreCommandBuild"]);
                Assert.Equal(1, metrics["coreBorderGeometry"]);
                Assert.Equal(1, metrics["pdfSave"]);
                Assert.True(metrics.GetValueOrDefault("pagePayloadMax", metrics.GetValueOrDefault("pagePayloadActive")) <= 1);
            }
            finally
            {
                Core.Rendering.ConversionMetrics.Observer = null;
            }
        }
    }

    [Fact]
    public async Task Reader_public_views_return_original_core_cells_at_layout_boundary()
    {
        using var input = new MemoryStream(Workbook());
        using var prepared = await Rendering.WorkbookInputPreparer.ReadAsync(input, new WorkbookInputOptions(), CancellationToken.None);
        var reader = new Excel.ExcelReader();
        var core = reader.ReadCore(prepared, null).Sheets[0];
        var view = CoreIntegration.CoreModelAdapter.ToPublic(core, new Excel.StylePool());
        Assert.Same(core.Cells, CoreIntegration.CoreModelAdapter.ToCore(view).Cells);
        Assert.Same(core.Columns, CoreIntegration.CoreModelAdapter.ToCore(view).Columns);
        Assert.Equal(core.Cells.Keys.Select(CoreIntegration.CoreModelAdapter.ToPublic), view.Cells.Keys);
        var imageBytes = new byte[] { 1, 2, 3 };
        var page = new Layout.RenderPage(1, [], [new(new(1, 2, 3, 4), imageBytes)]);
        Assert.Same(imageBytes, Assert.Single(CoreIntegration.CorePageAdapter.ToCore(page).Images!).ImageBytes);
    }

    [Fact]
    public async Task Different_physical_fonts_and_full_save_failure_do_not_poison_the_next_session()
    {
        var bytes = Workbook();
        var mono = Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansMono-Regular.ttf");
        byte[]? firstFont = null;
        foreach (var iteration in new[] { 0, 1 })
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            await Core.ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = mono });
            output.Position = 0;
            using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            var fonts = pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            var font = fonts.Elements.GetDictionary(fonts.Elements.Keys.First())!;
            var descendant = font.Elements.GetArray("/DescendantFonts")?.Elements.GetDictionary(0) ?? font;
            var embedded = descendant.Elements.GetDictionary("/FontDescriptor")!.Elements.GetDictionary("/FontFile2")!.Stream.UnfilteredValue;
            var widths = descendant.Elements.GetArray("/W")!.Elements.OfType<global::PdfSharp.Pdf.PdfArray>()
                .SelectMany(array => array.Elements).OfType<global::PdfSharp.Pdf.PdfInteger>().Select(width => width.Value).ToArray();
            Assert.NotEmpty(widths);
            Assert.All(widths, width => Assert.Equal(600, width));
            if (firstFont is not null) Assert.Equal(firstFont, embedded);
            firstFont = embedded;
            if (iteration == 0)
            {
                await ConvertFull(bytes);
                using var failedInput = new MemoryStream(bytes);
                using var failedSink = new FailingSink();
                await Assert.ThrowsAnyAsync<Exception>(() => ExcelConverter.RenderAsync(failedInput, new RenderRequest
                {
                    OutputFormat = OutputFormat.Pdf,
                    FontOptions = new() { AllowSystemFonts = false },
                }, failedSink));
                Assert.Null(GlobalFontSettings.FontResolver);
            }
        }

        await ConvertFull(bytes);
    }

    [Fact]
    public void Expired_font_snapshots_are_collectible_while_owned_runs_keep_their_snapshot()
    {
        var expired = RegisterUnownedSnapshot();
        var retained = new ResolvedFont("retained", 400, false, "memory") { FaceId = Guid.NewGuid().ToString(), FontData = new byte[8192] };
        var retainedKey = PdfSharp.PdfSharpFontResolver.RegisterResolvedFont(retained);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(expired.Owner.IsAlive);
        Assert.False(expired.Bytes.IsAlive);
        PdfSharp.PdfSharpFontResolver.RemoveExpiredRegistrations();
        var resolver = new PdfSharp.PdfSharpFontResolver();
        Assert.Null(resolver.GetFont(expired.Key));
        Assert.Equal(retained.FontData, resolver.GetFont(retainedKey));
        GC.KeepAlive(retained);
    }

    [Fact]
    public async Task Fifty_alternating_conversions_release_font_image_and_session_owners()
    {
        var bytes = Workbook(images: true);
        var previousImages = Core.Rendering.ImageResources.Current;
        var created = 0d;
        var disposed = 0d;
        Core.Rendering.ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "typefaceCreated") created += value;
            if (name == "typefaceDisposed") disposed += value;
        };
        try
        {
            for (var iteration = 0; iteration < 50; iteration++)
            {
                if (iteration % 2 == 0) await ConvertCore(bytes);
                else await ConvertFull(bytes);
                Assert.Equal(created, disposed);
                Assert.Null(GlobalFontSettings.FontResolver);
                Assert.Null(Rendering.RenderResourceSession.Current);
                Assert.Same(previousImages, Core.Rendering.ImageResources.Current);
            }

            Assert.True(created >= 50);
        }
        finally
        {
            Core.Rendering.ConversionMetrics.Observer = null;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (string Key, WeakReference Owner, WeakReference Bytes) RegisterUnownedSnapshot()
    {
        var font = new ResolvedFont("expired", 400, false, "memory") { FaceId = Guid.NewGuid().ToString(), FontData = new byte[8192] };
        var key = PdfSharp.PdfSharpFontResolver.RegisterResolvedFont(font);
        return (key, new WeakReference(font), new WeakReference(new PdfSharp.PdfSharpFontResolver().GetFont(key)!));
    }

    private static Core.PdfExportOptions CoreOptions() => new()
    {
        FontFilePath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"),
    };

    private static byte[] Workbook(bool images = false)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Report");
        sheet.Cell(1, 1).Value = "日本語 ABC";
        sheet.Cell(1, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Double;
        sheet.Style.Font.FontName = "Noto Sans JP";
        if (images)
        {
            using var bitmap = new global::SkiaSharp.SKBitmap(4, 4);
            bitmap.Erase(global::SkiaSharp.SKColors.Blue);
            using var png = bitmap.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var picture = png.AsStream();
            sheet.AddPicture(picture).MoveTo(sheet.Cell(1, 1));
        }

        using var input = new MemoryStream();
        book.SaveAs(input);
        return input.ToArray();
    }

    private static async Task ConvertCore(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var result = await Core.ExcelConverter.ConvertAsync(input, output, CoreOptions());
        Assert.Equal(1, result.PageCount);
        output.Position = 0;
        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    private static async Task ConvertFull(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var sink = new Sink();
        var result = await ExcelConverter.RenderAsync(input, new RenderRequest
        {
            OutputFormat = OutputFormat.Pdf,
            FontOptions = new FontOptions { AllowSystemFonts = false },
        }, sink);
        Assert.Single(result.Pages);
        sink.Output.Position = 0;
        using var document = PdfReader.Open(sink.Output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        sink.Output.Dispose();
    }

    private sealed class Sink : IRenderOutputSink
    {
        public MemoryStream Output { get; } = new();
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(Output);
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
    }

    private sealed class FailingSink : IRenderOutputSink, IDisposable
    {
        private readonly FailingOutput output = new();
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(output);
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
        public void Dispose() => output.Dispose();
    }

    private sealed class FailingOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Injected save failure.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Injected save failure.");
        public override void WriteByte(byte value) => throw new IOException("Injected save failure.");
    }
}
