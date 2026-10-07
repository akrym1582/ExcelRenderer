using ClosedXML.Excel;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Verifies optimized lookup boundaries, resource ownership and direct PDF assembly.</summary>
public sealed class PerformanceRegressionTests
{
    /// <summary>Sparse geometry agrees with the original linear interpretation, including zero-size axes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Sparse_geometry_preserves_boundaries(double defaultSize)
    {
        var random = new Random(7241);
        var overrides = Enumerable.Range(1, 200).Where(_ => random.Next(3) == 0)
            .ToDictionary(index => index, _ => random.Next(4) == 0 ? 0d : random.Next(1, 120) / 4d);
        var sheet = new ReportSheet("geometry", new Dictionary<CellAddress, ReportCell>(),
            new Dictionary<int, ColumnDefinition>(),
            overrides.ToDictionary(pair => pair.Key, pair => new RowDefinition(pair.Value, pair.Value == 0)),
            [], new(100, 100)) { DefaultRowHeight = defaultSize };
        var geometry = new SheetGeometry(sheet);
        double Size(int index) => overrides.GetValueOrDefault(index, defaultSize);
        double Start(int index) => Enumerable.Range(1, Math.Max(0, index - 1)).Sum(Size);
        for (var row = 1; row <= 400; row++)
        {
            Assert.Equal(Start(row), geometry.RowStart(row), 8);
        }

        Assert.Equal(0, geometry.RowStart(0));
        Assert.Equal(1, geometry.RowAt(-1));
        Assert.Equal(1, geometry.RowAt(0));
        foreach (var position in Enumerable.Range(1, 250).SelectMany(row => new[] { Start(row), Start(row) + 0.125 }))
        {
            var expected = 1;
            var origin = 0d;
            foreach (var pair in overrides.OrderBy(pair => pair.Key))
            {
                var gap = pair.Key - expected;
                if (gap > 0 && defaultSize > 0 && position < origin + gap * defaultSize)
                {
                    expected += (int)Math.Floor((position - origin) / defaultSize);
                    goto Check;
                }

                origin += gap * defaultSize;
                expected = pair.Key;
                if (position < origin + pair.Value)
                {
                    goto Check;
                }

                origin += pair.Value;
                expected++;
            }

            expected = defaultSize > 0 ? expected + (int)Math.Floor((position - origin) / defaultSize) : expected;
        Check:
            Assert.Equal(position <= 0 ? 1 : expected, geometry.RowAt(position));
        }
    }

    /// <summary>A sparse merged interval is found even if its top-left lies before the page band.</summary>
    [Fact]
    public void Band_index_preserves_half_open_intersections()
    {
        var index = new BandIndex<string>([("long", 1, 1000000), ("before", 0, 5), ("atEnd", 10, 11), ("inside", 7, 9)]);
        Assert.Equal(new[] { "inside", "long" }, index.Query(5, 10).OrderBy(value => value));
        Assert.Equal(new[] { "long" }, index.Query(900000, 900001));
        Assert.Empty(index.Query(1000000, double.PositiveInfinity));
    }

    /// <summary>Canonical styles retain data-dependent alignment while sharing equal values.</summary>
    [Fact]
    public void Reader_shares_equal_styles_without_merging_general_alignment()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("styles");
        sheet.Cell("A1").Value = "text";
        sheet.Cell("A2").Value = "other";
        sheet.Cell("A3").Value = 42;
        sheet.Range("A1:A3").Style.Border.BottomBorder = XLBorderStyleValues.Double;
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        var model = new ExcelReader().Read(input.ToArray(), null).Sheets[0];
        var first = model.Cells[new(1, 1)].Style;
        var second = model.Cells[new(2, 1)].Style;
        var numeric = model.Cells[new(3, 1)].Style;
        Assert.Same(first, second);
        Assert.Equal(HorizontalAlignment.Right, numeric.HorizontalAlignment);
        Assert.Same(first.Font, numeric.Font);
        Assert.Same(first.Border, numeric.Border);
    }

    /// <summary>Multiple print areas do not measure text from rows absent from their layout.</summary>
    [Fact]
    public void Text_measurement_skips_rows_outside_layout()
    {
        var sheet = new ReportSheet("range", new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("visible", CellStyle.Default),
            [new(1000, 1)] = new("outside", CellStyle.Default),
        }, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(100, 100));
        var context = new ReportLayoutContext(sheet, new PdfSharpTextMeasurer());
        context.ColumnLayouts[1] = new(1, 0, 50);
        context.RowLayouts[1] = new(1, 0, 15);
        new TextMeasurePass().Execute(context);
        Assert.True(context.TextLayouts.ContainsKey(new(1, 1)));
        Assert.False(context.TextLayouts.ContainsKey(new(1000, 1)));
    }

    /// <summary>Registration invalidates resolved text and layout caches while preserving UTF-16 source offsets.</summary>
    [Fact]
    public void Register_invalidates_text_and_layout_caches()
    {
        var manager = new FontManager(new()
        {
            AllowSystemFonts = false,
            UseFontPack = false,
            Registrations = [new("base", Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"))],
            FallbackFamilies = ["base"],
        });
        var request = new FontRequest("alias");
        var before = manager.ResolveTextRuns("ABC", request);
        var measurer = new PdfSharpTextMeasurer(manager, cacheLayouts: true);
        using var resources = new ConversionFontResources();
        var beforeLayout = measurer.Layout("ABC", new("alias"), 100, false);
        manager.Register("alias", Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansMono-Regular.ttf"));
        var after = manager.ResolveTextRuns("ABC", request);
        var afterLayout = measurer.Layout("ABC", new("alias"), 100, false);
        Assert.NotEqual(before[0].Font.FaceId, after[0].Font.FaceId);
        Assert.NotEqual(beforeLayout.Lines[0].Runs[0].Run.Font.FaceId, afterLayout.Lines[0].Runs[0].Run.Font.FaceId);
        var text = "A葛\U000E0100B";
        var runs = manager.ResolveTextRuns(text, request);
        Assert.Equal(runs, manager.ResolveTextRuns(text, request));
        Assert.Contains(runs, run => run.MissingIvsGlyph && run.Utf16Start == 1 && run.SourceText == "葛\U000E0100");
    }

    /// <summary>A very large sparse rectangle searches only the existing row and column addresses.</summary>
    [Fact]
    public void Sparse_range_index_returns_only_existing_cells()
    {
        CellAddress[] addresses = [new(1, 1), new(1, 500), new(900000, 3), new(1000000, 16000)];
        var index = new CellRangeIndex(addresses);
        Assert.Equal(new[] { addresses[0], addresses[2] }, index.Query(new(new(1, 1), new(900000, 4))).ToArray());
        Assert.Empty(index.Query(new(new(2, 1), new(899999, 16000))));
    }

    /// <summary>Aliases share native resources; exceptions and cancellation restore the previous owner.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_faces_are_released_on_unwind(bool cancel)
    {
        Assert.Null(ConversionFontResources.Current);
        global::SkiaSharp.SKTypeface? face = null;
        Assert.ThrowsAny<Exception>((Action)(() =>
        {
            using var resources = new ConversionFontResources();
            face = resources.GetTypeface(OutputFixture.Face);
            Assert.Same(face, resources.GetTypeface(OutputFixture.Face with { Family = "alias" }));
            using (var lease = new TypefaceLease(OutputFixture.Face))
            {
                Assert.Same(face, lease.Typeface);
            }

            Assert.NotEqual(IntPtr.Zero, face.Handle);
            if (cancel)
            {
                throw new OperationCanceledException();
            }

            throw new InvalidOperationException();
        }));
        Assert.NotNull(face);
        Assert.Equal(IntPtr.Zero, face.Handle);
        Assert.Null(ConversionFontResources.Current);
    }

    /// <summary>Releasing conversion resources keeps a concurrently owned Skia font usable.</summary>
    [Fact]
    public void Font_cache_cleanup_preserves_active_font()
    {
        using var face = OutputFixture.Typeface();
        using var font = new global::SkiaSharp.SKFont(face, 12);
        var width = font.MeasureText("ABC");
        using (var resources = new ConversionFontResources())
        {
            using var lease = new TypefaceLease(OutputFixture.Face);
            using var temporary = new global::SkiaSharp.SKFont(lease.Typeface, 10);
            Assert.True(temporary.MeasureText("ABC") > 0);
        }

        Assert.Equal(width, font.MeasureText("ABC"));
        Assert.NotEqual(IntPtr.Zero, face.Handle);
    }

    /// <summary>Direct assembly saves once, retains empty pages and shares one embedded font across pages.</summary>
    [Fact]
    public async Task Converter_saves_once_and_reuses_font_stream()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("pages");
        for (var row = 1; row <= 120; row++)
        {
            sheet.Cell(row, 1).Value = "共通 ABC";
        }

        sheet.Style.Font.FontName = "Noto Sans JP";
        sheet.Rows(1, 120).Height = 30;
        sheet.PageSetup.PrintAreas.Add("A1:A120");
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        using var output = new MemoryStream();
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var result = await ExcelConverter.RenderAsync(input, Request(), new SingleStreamOutputSink(output));
            Assert.True(result.Pages.Count > 1);
            Assert.Equal(1, metrics["pdfSave"]);
            Assert.False(metrics.ContainsKey("pdfImport"));
            Assert.Equal(1, metrics["typefaceCreated"]);
            Assert.Equal(1, metrics["lineMetricsComputed"]);
            output.Position = 0;
            using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            Assert.Equal(result.Pages.Count, pdf.PageCount);
            var fonts = pdf.Pages.Cast<global::PdfSharp.Pdf.PdfPage>()
                .Select(page => page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!)
                .SelectMany(font => font.Elements.Values)
                .OfType<global::PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.ObjectID).Distinct().ToArray();
            Assert.Single(fonts);
            Assert.True(output.CanWrite);
            Assert.True(input.CanRead);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }

        Assert.Null(ConversionFontResources.Current);
    }

    /// <summary>Empty descriptors remain present with distinct paper sizes in a shared document.</summary>
    [Fact]
    public void Append_page_keeps_empty_pages_and_dimensions()
    {
        using var document = new global::PdfSharp.Pdf.PdfDocument();
        var renderer = new PdfSharpRenderer();
        renderer.AppendPage(document, new(100, 200), []);
        renderer.AppendPage(document, new(300, 400), []);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(100, document.Pages[0].Width.Point);
        Assert.Equal(400, document.Pages[1].Height.Point);
        Assert.Equal(document.Pages[1].MediaBox, document.Pages[1].CropBox);
    }

    /// <summary>Cancellation between pages aborts the sink and releases resources without closing caller streams.</summary>
    [Fact]
    public async Task Cancellation_between_pdf_pages_aborts_sink()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("cancel");
        sheet.Cell("A1").Value = "first";
        sheet.Cell("A120").Value = "last";
        sheet.Style.Font.FontName = "Noto Sans JP";
        sheet.Rows(1, 120).Height = 30;
        sheet.PageSetup.PrintAreas.Add("A1:A120");
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var sink = new ObservedSink(output);
        ConversionMetrics.Observer = (name, _) =>
        {
            if (name == "pdfPage.ms")
            {
                cancellation.Cancel();
            }
        };
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                ExcelConverter.RenderAsync(input, Request(), sink, cancellation.Token));
            Assert.IsType<OperationCanceledException>(sink.Error);
            Assert.False(sink.Completed);
            Assert.Equal(0, output.Length);
            Assert.True(output.CanWrite);
            Assert.True(input.CanRead);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }

        Assert.Null(ConversionFontResources.Current);
    }

    private static RenderRequest Request() => new()
    {
        OutputFormat = OutputFormat.Pdf,
        FontOptions = new()
        {
            AllowSystemFonts = false,
            UseFontPack = false,
            Registrations = [new FontRegistration("Noto Sans JP", Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"))],
            FallbackFamilies = ["Noto Sans JP"],
        },
    };
    private sealed class ObservedSink(Stream output) : IRenderOutputSink
    {
        internal Exception? Error { get; private set; }

        internal bool Completed { get; private set; }

        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(output);

        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
        {
            Completed = true;
            return default;
        }

        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken)
        {
            Error = error;
            return default;
        }
    }

}
