using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Abstractions;
using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using SkiaSharp;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class ColumnWidthMetricsTests
{
    private static string Mono => Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansMono-Regular.ttf");
    private static string Noto => Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
    private static string Collection => Path.Combine(AppContext.BaseDirectory, "Fonts", "PdfFormats", "collection-distinct.ttc");

    [Fact]
    public void Substituted_families_use_7px_and_do_not_change_layout()
    {
        var diagnostics = new DiagnosticCollector(new());
        var normal = new Core.Excel.NormalFontMetadata("Missing Original", 11);
        foreach (var file in new[] { Mono, Noto })
        {
            var manager = new SubstituteManager(file);
            Assert.Equal(7, new FullFontMetrics(manager, diagnostics).MaximumDigitWidth(normal, "data"));
            using var input = BoundaryWorkbook();
            var sheet = new ExcelReader(manager).Read(input).Sheets.Single();
            Assert.Equal(ColumnWidthCalculator.ToPoints(12, 7), sheet.Columns[1].Width);
        }

        Assert.Contains(diagnostics.ToArray(), item => item.Code == "MaximumDigitWidthFallback" && item.Message.Contains("SourceFontFile"));
    }

    [Fact]
    public void Explicit_MDW_stabilizes_columns_scale_bands_and_last_row_with_any_drawing_font()
    {
        foreach (var file in new[] { Mono, Noto })
        {
            using var input = BoundaryWorkbook();
            var sheet = new ExcelReader(new SubstituteManager(file)).Read(input, new() { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 16 } }).Sheets.Single();
            var plan = new ReportLayoutEngine(new FixedMeasurer()).Plan(sheet).Single();
            var page = Assert.Single(plan.Pages);
            Assert.Equal(ColumnWidthCalculator.ToPoints(12, 16), sheet.Columns[1].Width);
            Assert.Equal(540d / (5 * ColumnWidthCalculator.ToPoints(12, 16)), page.Scale, 8);
            var built = plan.Build(0, 1, 1, new FixedMeasurer());
            var last = built.Cells.Single(cell => cell.SourceAddress == new CellAddress(65, 1));
            Assert.Equal(36 + (64 * 12 * page.Scale), last.Bounds.Y, 6);
            Assert.Equal(12 * page.Scale, last.Bounds.Height, 6);
        }
    }

    [Fact]
    public void Exact_source_file_uses_Normal_size_and_keeps_Noto_for_drawing()
    {
        var normal = new Core.Excel.NormalFontMetadata("Noto Sans Mono", 11);
        var manager = new FontManager(new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [Mono], FallbackFamilies = [] });
        var expected = Measure(Mono, 0, 11);
        Assert.Equal(expected, new FullFontMetrics(manager, null).MaximumDigitWidth(normal, "data"));
        var drawing = new SubstituteManager(Noto);
        var options = new ExcelReadOptions { ColumnWidthMetrics = new() { SourceFontFile = Mono } };
        Assert.Equal(expected, new FullFontMetrics(drawing, null, options).MaximumDigitWidth(normal, "data"));
        Assert.Empty(drawing.Requests);
        Assert.Equal(Noto, drawing.Resolve(new(normal.Family)).FilePath);
    }

    [Fact]
    public void Missing_Normal_metadata_preserves_legacy_7px_without_polluting_known_Normal_or_override()
    {
        var manager = new SubstituteManager(Mono);
        var cache = new FullFontMetrics.MetricCache();
        var legacy = new ExcelReadOptions { ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy }, ColumnWidthMetricsCache = cache };
        var metrics = new FullFontMetrics(manager, null, legacy);
        Assert.Equal(7, metrics.MaximumDigitWidth(null, "data"));
        Assert.Equal(Measure(Mono, 0, 11), metrics.MaximumDigitWidth(new("Calibri", 11), "data"));
        Assert.Equal(7, metrics.MaximumDigitWidth(null, "data"));
        Assert.Single(manager.Requests);
        var explicitOptions = legacy with { ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy, MaximumDigitWidthPixels96Dpi = 13 } };
        Assert.Equal(13, new FullFontMetrics(manager, null, explicitOptions).MaximumDigitWidth(null, "data"));
        Assert.Equal(7, new FullFontMetrics(manager, null).MaximumDigitWidth(null, "data"));
    }

    [Fact]
    public void Cache_separates_source_legacy_override_style_and_collection_face()
    {
        var cache = new FullFontMetrics.MetricCache();
        var normal = new Core.Excel.NormalFontMetadata("Unavailable", 11);
        var manager = new SubstituteManager(Mono);
        foreach (var mode in new[] { ColumnWidthMetricsMode.SourceFont, ColumnWidthMetricsMode.RenderingFontLegacy, ColumnWidthMetricsMode.SourceFont })
        {
            var options = new ExcelReadOptions { ColumnWidthMetrics = new() { Mode = mode }, ColumnWidthMetricsCache = cache };
            Assert.Equal(mode == ColumnWidthMetricsMode.SourceFont ? 7 : Measure(Mono, 0, 11), new FullFontMetrics(manager, null, options).MaximumDigitWidth(normal, "data"));
        }

        var explicitOptions = new ExcelReadOptions { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 17 }, ColumnWidthMetricsCache = cache };
        Assert.Equal(17, new FullFontMetrics(manager, null, explicitOptions).MaximumDigitWidth(normal, "data"));
        foreach (var index in new[] { 1, 0, 1 })
        {
            var source = new ExcelReadOptions { ColumnWidthMetrics = new() { SourceFontFile = Collection, SourceFontCollectionIndex = index }, ColumnWidthMetricsCache = cache };
            source.Validate();
            Assert.Equal(Measure(Collection, index, 11), new FullFontMetrics(manager, null, source).MaximumDigitWidth(normal, "data"));
        }

        Assert.NotEqual(Measure(Collection, 0, 11), Measure(Collection, 1, 11));
    }

    [Fact]
    public async Task Source_file_measurement_and_selected_page_preflight_share_Noto_rendering()
    {
        using var input = BoundaryWorkbook();
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            ReadOptions = new() { ColumnWidthMetrics = new() { SourceFontFile = Collection, SourceFontCollectionIndex = 1 } },
            Selection = new() { Pages = [1] },
            FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [Noto], FallbackFamilies = ["Noto Sans JP"] },
        }, new SingleStreamOutputSink(output));
        Assert.Single(result.Pages);
        var measurement = Assert.Single(result.Diagnostics, item => item.Code == "MaximumDigitWidthResolved");
        Assert.Contains("source=source-file", measurement.Message);
        Assert.Contains("face 1", measurement.Message);
        using var pdf = PdfTestFile.Open(new MemoryStream(output.ToArray()));
        Assert.Single(pdf.Pages);
        var resources = pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        Assert.All(resources.Elements.Keys, key => Assert.Contains("NotoSansJP", resources.Elements.GetDictionary(key)!.Elements.GetName("/BaseFont")));
    }

    [Fact]
    public void Exact_lookup_does_not_search_unpermitted_system_fonts_or_trust_external_family_labels()
    {
        var manager = new FontManager(new() { AllowSystemFonts = false, UseFontPack = false, FontDirectories = [], FontFiles = [], FallbackFamilies = [] });
        Assert.Null(manager.ResolveExact(new("Noto Sans JP")));
        var fakeIdentity = new FontManager(new() { AllowSystemFonts = false, UseFontPack = false, Registrations = [new FontRegistration("Missing Original", Mono)], FallbackFamilies = [] });
        Assert.Equal(7, new FullFontMetrics(fakeIdentity, null).MaximumDigitWidth(new("Missing Original", 11), "data"));
    }

    [Fact]
    public void Metric_decisions_are_reused_across_preflight_reads_and_reported_for_each_sheet()
    {
        var manager = new SubstituteManager(Mono);
        var diagnostics = new DiagnosticCollector(new());
        var options = new ExcelReadOptions { ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy }, ColumnWidthMetricsCache = new() };
        var normal = new Core.Excel.NormalFontMetadata("Missing Original", 11);
        var first = new FullFontMetrics(manager, null, options).MaximumDigitWidth(normal, "first");
        Assert.Equal(first, new FullFontMetrics(manager, diagnostics, options).MaximumDigitWidth(normal, "first"));
        Assert.Equal(first, new FullFontMetrics(manager, diagnostics, options).MaximumDigitWidth(normal, "second"));
        Assert.Single(manager.Requests);
        Assert.Equal(2, diagnostics.ToArray().Count);
        Assert.All(diagnostics.ToArray(), item => Assert.Contains("source=legacy", item.Message));
    }

    [Fact]
    public void Normal_style_weight_and_italic_are_forwarded_and_cells_do_not_set_MDW()
    {
        using var input = BoundaryWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var normal = package.WorkbookPart!.WorkbookStylesPart!.Stylesheet.Fonts!.Elements<S.Font>().First();
            normal.Append(new S.Bold(), new S.Italic());
        }

        input.Position = 0;
        var manager = new SubstituteManager(Mono);
        _ = new ExcelReader(manager).Read(input);
        Assert.Contains(manager.Requests, request => request.Weight == 700 && request.Italic);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.9)]
    [InlineData(-1)]
    public void Invalid_MDW_is_rejected_before_input_is_opened(double width)
    {
        var options = new ExcelReadOptions { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = width } };
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExcelReader().Read("missing-workbook.xlsx", options));
    }

    [Fact]
    public void Invalid_explicit_source_settings_fail_even_with_MDW_override()
    {
        foreach (var settings in new[]
        {
            new ColumnWidthMetricsOptions { SourceFontCollectionIndex = 1 },
            new ColumnWidthMetricsOptions { SourceFontFile = Mono, SourceFontCollectionIndex = -1 },
            new ColumnWidthMetricsOptions { SourceFontFile = Mono, SourceFontCollectionIndex = 1 },
            new ColumnWidthMetricsOptions { SourceFontFile = Collection, SourceFontCollectionIndex = 50 },
            new ColumnWidthMetricsOptions { SourceFontFile = "missing-font.ttf", MaximumDigitWidthPixels96Dpi = 9 },
            new ColumnWidthMetricsOptions { SourceFontFile = Mono, Mode = ColumnWidthMetricsMode.RenderingFontLegacy },
        })
        {
            Assert.ThrowsAny<ArgumentException>(() => new ExcelReadOptions { ColumnWidthMetrics = settings }.Validate());
        }

        var invalid = Path.GetTempFileName();
        try
        {
            File.WriteAllText(invalid, "not a font");
            Assert.Throws<ArgumentException>(() => new ExcelReadOptions { ColumnWidthMetrics = new() { SourceFontFile = invalid } }.Validate());
        }
        finally
        {
            File.Delete(invalid);
        }
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    [InlineData(OutputFormat.PostScript)]
    public async Task Combined_fixture_uses_one_common_page_plan_for_all_outputs(OutputFormat format)
    {
        using var input = BoundaryWorkbook();
        var sink = new MemorySink();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = format,
            Dpi = 36,
            ReadOptions = new() { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 16 } },
            FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [Noto], FallbackFamilies = ["Noto Sans JP"] },
            DiagnosticOptions = new() { EnableLayoutWarnings = true },
        }, sink);
        Assert.Single(result.Pages);
        Assert.Contains(result.Diagnostics, item => item.Code == "MaximumDigitWidthResolved" && item.Message.Contains("source=override"));
        Assert.Contains(result.Diagnostics, item => item.Code == "PaginationMetrics" && item.Message.Contains("780pt") && item.Message.Contains("pages 1x1"));
        Assert.Contains(result.Diagnostics, item => item.Code == "SavedPageBreaks" && item.Message.Contains("excluded automatic row breaks: 1") && item.Message.Contains("saved print scale percent: 100"));
        Assert.True(sink.Output.Length > 0);
        if (format == OutputFormat.Pdf)
        {
            using var pdf = PdfTestFile.Open(new MemoryStream(sink.Output.ToArray()));
            Assert.Single(pdf.Pages);
            Assert.Contains("row 65", pdf.Pages[0].Text);
        }
    }

    [Fact]
    public void Boundary_fixture_legacy_narrow_font_produces_two_pages_source_metrics_produce_one()
    {
        using var input = BoundaryWorkbook();
        var reader = new ExcelReader(new SubstituteManager(Mono));
        var legacy = reader.Read(input, new() { ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy } }).Sheets.Single();
        input.Position = 0;
        var source = reader.Read(input, new() { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 16 } }).Sheets.Single();
        Assert.Equal(2, new ReportLayoutEngine(new FixedMeasurer()).Plan(legacy).Single().Pages.Count);
        Assert.Single(new ReportLayoutEngine(new FixedMeasurer()).Plan(source).Single().Pages);
    }

    private static double Measure(string path, int index, double size)
    {
        using var resources = new ConversionFontResources();
        using var font = new SKFont(resources.GetTypeface(new("physical", 400, false, path) { FontCollectionIndex = index }), (float)(size * 96 / 72));
        return Math.Max(1, Math.Round(Enumerable.Range(0, 10).Max(digit => font.MeasureText(digit.ToString())), MidpointRounding.AwayFromZero));
    }

    private static MemoryStream BoundaryWorkbook()
    {
        var stream = SdkWorkbook.Create(new S.Worksheet(new S.SheetData()), SdkWorkbook.Styles());
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("xl/worksheets/sheet1.xml")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());
            var rows = string.Concat(Enumerable.Range(1, 65).Select(row => $"<row r=\"{row}\" ht=\"12\"><c r=\"A{row}\" t=\"inlineStr\"><is><t>row {row}</t></is></c><c r=\"E{row}\" t=\"inlineStr\"><is><t>end</t></is></c></row>"));
            writer.Write($"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><sheetFormatPr defaultRowHeight=\"15\"/><cols><col min=\"1\" max=\"5\" width=\"12\"/></cols><sheetData>{rows}</sheetData><pageMargins left=\"0.5\" right=\"0.5\" top=\"0.5\" bottom=\"0.5\" header=\"0\" footer=\"0\"/><pageSetup paperSize=\"1\" fitToWidth=\"1\" fitToHeight=\"0\"/><rowBreaks><brk id=\"20\"/></rowBreaks></worksheet>");
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class SubstituteManager(string path) : IFontManager
    {
        internal List<FontRequest> Requests { get; } = [];
        public ResolvedFont Resolve(FontRequest request)
        {
            Requests.Add(request);
            return new("Substitute", 400, false, path);
        }

        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request) => [new(text, Resolve(request))];
    }

    private sealed class FixedMeasurer : ITextMeasurer
    {
        public TextSize Measure(string text, Model.FontStyle font, double availableWidth, bool wrap) => new(10, 10);
    }

    private sealed class MemorySink : IRenderOutputSink
    {
        internal MemoryStream Output { get; } = new();
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(Output);
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
    }
}
