using System.IO.Compression;
using ClosedXML.Excel;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Checks payload lifetimes, bounded buffers, spool ownership, and native leases.</summary>
public sealed class Phase3MemoryTests
{
    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    public async Task Selected_pages_preflight_and_render_with_one_active_payload(OutputFormat format)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Pages");
        sheet.Style.Font.FontName = "Noto Sans JP";
        for (var row = 1; row <= 100; row++)
        {
            sheet.Cell(row, 1).Value = "page " + row;
        }

        sheet.Rows().Height = 40;
        sheet.PageSetup.PrintAreas.Add("A1:A100");
        using var input = new MemoryStream();
        book.SaveAs(input);
        input.Position = 0;
        var sink = new ObservedSink();
        var active = 0d;
        var maximum = 0d;
        var builds = 0;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "pagePayloadActive")
            {
                active += value;
                maximum = Math.Max(maximum, active);
                Assert.InRange(active, 0, 1);
            }
            else if (name == "pageBuild.ms")
            {
                builds++;
            }
        };
        try
        {
            var result = await ExcelConverter.RenderAsync(input, Request(format) with
            {
                Selection = new() { Pages = [3, 1, 3] },
            }, sink);
            Assert.Equal(new[] { 1, 3 }, result.Pages.Select(page => page.DocumentPageNumber));
            Assert.Equal(new int?[] { 1, 2 }, result.Pages.Select(page => page.OutputPageNumber));
            Assert.Equal(1, maximum);
            Assert.Equal(0, active);
            Assert.Equal(4, builds);
            Assert.Equal(format == OutputFormat.Pdf ? 1 : 2, sink.OpenCount);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(100_000)]
    public void Buffer_spills_before_growth_and_preserves_seek_overwrite(int count)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using (var stream = new SpillableBufferStream(new() { MemoryThresholdBytes = 8, TemporaryDirectory = directory.FullName }))
            {
                var bytes = Enumerable.Range(0, count).Select(i => (byte)i).ToArray();
                stream.Write(bytes, 0, count);
                Assert.Equal(count > 8, stream.HasSpilled);
                Assert.InRange(stream.MemoryCapacity, 0, 8);
                if (count > 0)
                {
                    stream.Position = 0;
                    stream.WriteByte(255);
                    bytes[0] = 255;
                }

                using var reader = stream.OpenRead();
                using var copy = new MemoryStream();
                reader.CopyTo(copy);
                Assert.Equal(bytes, copy.ToArray());
                Assert.Equal(count, stream.Length);
            }

            Assert.Empty(directory.GetFiles());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void No_temp_rejects_oversize_before_allocating_and_set_length_is_bounded()
    {
        using var stream = new SpillableBufferStream(new() { MemoryThresholdBytes = 8, AllowTemporaryFiles = false });
        stream.SetLength(8);
        Assert.Throws<InvalidDataException>(() => stream.Write(new byte[100_000], 0, 100_000));
        Assert.Equal(8, stream.Length);
        Assert.InRange(stream.MemoryCapacity, 0, 8);
        Assert.Throws<InvalidDataException>(() => stream.SetLength(9));
    }

    [Fact]
    public async Task Input_spool_opens_independent_readers_and_cleans_up_without_closing_input()
    {
        using var input = new MemoryStream();
        input.Write(new byte[13]);
        using (var zip = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("test.xml").Open();
            entry.Write(new byte[1000]);
        }

        // ZipArchive seeks relative to the containing stream, so test the current-position contract
        // with a standalone ZIP copied behind a prefix.
        using var standalone = new MemoryStream();
        using (var zip = new ZipArchive(standalone, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("test.xml").Open();
            entry.Write(new byte[1000]);
        }

        input.SetLength(13);
        input.Position = 13;
        input.Write(standalone.ToArray());
        input.Position = 13;
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using (var owner = await WorkbookInputPreparer.ReadAsync(input,
                new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory.FullName }, default))
            {
                Assert.Single(directory.GetFiles());
                using var first = owner.OpenRead();
                using var second = owner.OpenRead();
                Assert.Equal(first.ReadByte(), second.ReadByte());
                first.Position = first.Length - 1;
                Assert.Equal(1, second.Position);
                using var zip = new ZipArchive(second, ZipArchiveMode.Read, leaveOpen: true);
                Assert.Single(zip.Entries);
            }

            Assert.True(input.CanRead);
            Assert.Empty(directory.GetFiles());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Image_cache_pins_active_entries_evicts_idle_entries_and_restores_outer_scope()
    {
        using var outer = new ImageResources(8);
        var bytesA = new byte[] { 1 };
        var bytesB = new byte[] { 2 };
        var a = new DisposableImage();
        var b = new DisposableImage();
        using var leaseA = outer.Acquire(bytesA, () => (a, 8))!;
        using (var again = outer.Acquire<DisposableImage>(bytesA, () => throw new Exception("cache miss")))
        {
            Assert.Same(a, again!.Value);
        }

        using (var leaseB = outer.Acquire(bytesB, () => (b, 8)))
        {
            Assert.False(a.Disposed);
        }

        Assert.True(b.Disposed);
        leaseA.Dispose();
        using (var leaseB = outer.Acquire(bytesB, () => (new DisposableImage(), 8)))
        {
            Assert.True(a.Disposed);
        }

        using (var inner = new ImageResources())
        {
            Assert.Same(inner, ImageResources.Current);
        }

        Assert.Same(outer, ImageResources.Current);
    }

    [Fact]
    public void Optional_font_metadata_matches_each_discovered_physical_font()
    {
        foreach (var resource in OptionalFontPack.Fonts)
        {
            using var stream = new MemoryStream(resource.Load(), false);
            using var face = global::SkiaSharp.SKTypeface.FromStream(stream);
            Assert.NotNull(face);
            Assert.Equal(resource.Family, face.FamilyName);
            Assert.Equal(400, face.FontStyle.Weight);
            Assert.Equal(global::SkiaSharp.SKFontStyleSlant.Upright, face.FontStyle.Slant);
        }
    }

    [Theory]
    [InlineData(10, 10, false, false, false, false, true)]
    [InlineData(20, 10, false, false, false, false, false)]
    [InlineData(10, 20, false, false, false, false, false)]
    [InlineData(5, 5, true, true, false, false, true)]
    [InlineData(0, 10, true, true, false, false, false)]
    [InlineData(5, 5, false, true, false, false, false)]
    [InlineData(10, 0, false, false, false, true, true)]
    [InlineData(0, 10, false, false, true, false, true)]
    [InlineData(0, 0, false, false, true, true, true)]
    public void Page_cell_selection_preserves_fixed_boundary_merge_and_title_cases(double x, double y, bool requested, bool merged, bool titleColumn, bool titleRow, bool expected)
    {
        var selection = new PageCellSelection(new(10, 20), new(10, 20), titleColumn ? [1] : [], titleRow ? [1] : [], 5, 5, requested);
        Assert.Equal(expected, selection.Contains(new(1, 1), new(x, y, 10, 10), merged));
    }

    [Theory]
    [InlineData(9.9999998, false)]
    [InlineData(9.99999995, true)]
    [InlineData(10, true)]
    public void Page_title_repetition_preserves_epsilon_boundary(double start, bool expected)
    {
        var selection = new PageCellSelection(new(start, 20), new(start, 20), [1], [1], 10, 10, false);
        Assert.Equal(expected, selection.RepeatColumns);
        Assert.Equal(expected, selection.RepeatRows);
        Assert.Equal(expected, selection.Contains(new(1, 1), new(0, 0, 5, 5), false));
    }

    [Fact]
    public void Page_without_titles_retains_negative_infinity_repeat_flags_without_membership()
    {
        var selection = new PageCellSelection(new(10, 20), new(10, 20), [], [], double.NegativeInfinity, double.NegativeInfinity, false);
        Assert.True(selection.RepeatColumns);
        Assert.True(selection.RepeatRows);
        Assert.False(selection.Contains(new(1, 1), new(0, 0, 5, 5), false));
    }

    [Theory]
    [InlineData(OutputFormat.Pdf, ImageLayoutMode.Paginated, false)]
    [InlineData(OutputFormat.Pdf, ImageLayoutMode.Paginated, true)]
    [InlineData(OutputFormat.Png, ImageLayoutMode.Paginated, false)]
    [InlineData(OutputFormat.Svg, ImageLayoutMode.Paginated, true)]
    [InlineData(OutputFormat.Png, ImageLayoutMode.Continuous, false)]
    [InlineData(OutputFormat.Svg, ImageLayoutMode.Continuous, true)]
    public async Task Empty_and_header_only_pages_keep_source_number_and_canvas_metadata(OutputFormat format, ImageLayoutMode layout, bool header)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Empty");
        if (header)
        {
            sheet.PageSetup.Header.Left.AddText("header 1 / 1");
        }

        using var input = new MemoryStream();
        book.SaveAs(input);
        input.Position = 0;
        var result = await ExcelConverter.RenderAsync(input, Request(format) with { ImageLayout = layout }, new ObservedSink());
        var page = Assert.Single(result.Pages);
        Assert.Equal(1, page.SourcePageNumber);
        Assert.Equal(1, page.DocumentPageNumber);
        Assert.Equal(1, page.OutputPageNumber);
        if (layout == ImageLayoutMode.Continuous)
        {
            Assert.Equal(1, page.WidthPoints);
            Assert.Equal(1, page.HeightPoints);
        }
        else
        {
            Assert.True(page.WidthPoints > 100);
            Assert.True(page.HeightPoints > 100);
        }
    }

    [Fact]
    public async Task Paginated_merged_titles_match_public_layout_positions_borders_and_converter_pixels()
    {
        using var book = new XLWorkbook();
        var worksheet = book.AddWorksheet("Titles");
        worksheet.Style.Font.FontName = "Noto Sans JP";
        worksheet.Column(1).Width = 12;
        worksheet.Column(2).Width = 12;
        worksheet.Rows(1, 80).Height = 25;
        worksheet.Range("A1:B1").Merge().Value = "merged title";
        worksheet.Range("A1:B1").Style.Border.OutsideBorder = XLBorderStyleValues.Double;
        for (var row = 2; row <= 80; row++)
        {
            worksheet.Cell(row, 1).Value = "body " + row;
        }

        worksheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
        worksheet.PageSetup.PrintAreas.Add("A1:B40");
        worksheet.PageSetup.PrintAreas.Add("A41:B80");
        using var input = new MemoryStream();
        book.SaveAs(input);
        var manager = new FontManager(Request(OutputFormat.Png).FontOptions);
        using var fonts = new ConversionFontResources();
        input.Position = 0;
        var sheet = Assert.Single(new ExcelReader(manager).Read(input).Sheets);
        var document = new ReportLayoutEngine(new PdfSharpTextMeasurer(manager, cacheLayouts: true)).Layout(sheet);
        Assert.True(document.Pages.Count > 2);
        foreach (var page in document.Pages)
        {
            Assert.Equal(page.Cells.Count, page.Cells.Select(cell => cell.SourceAddress).Distinct().Count());
            var title = Assert.Single(page.Cells, cell => cell.Cell.Text == "merged title");
            Assert.NotEmpty(title.MergedBorders!);
            Assert.True(title.Bounds.Y < page.Cells.First(cell => cell.Cell.Text!.StartsWith("body", StringComparison.Ordinal)).Bounds.Y);
        }

        var sink = new CapturedPageSink();
        input.Position = 0;
        var result = await ExcelConverter.RenderAsync(input, Request(OutputFormat.Png), sink);
        Assert.Equal(document.Pages.Count, result.Pages.Count);
        Assert.Equal(Enumerable.Range(1, document.Pages.Count), result.Pages.Select(page => page.SourcePageNumber));
        var commands = new DrawCommandGeneratorPass().Generate(document);
        for (var index = 0; index < document.Pages.Count; index++)
        {
            using var expected = new MemoryStream();
            new PngRenderer(manager).RenderPage(commands.Where(command => command.PageNumber == index + 1).ToArray(), sheet.PageSettings, expected, Request(OutputFormat.Png).Dpi);
            using var expectedBitmap = global::SkiaSharp.SKBitmap.Decode(expected.ToArray());
            using var actualBitmap = global::SkiaSharp.SKBitmap.Decode(sink.Outputs[index].ToArray());
            Assert.Equal(expectedBitmap.Pixels, actualBitmap.Pixels);
        }
    }

    [Fact]
    public void Continuous_layer_selection_measures_only_text_and_keeps_complete_empty_merged_cells()
    {
        var border = new BorderStyle(Top: new());
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("text", CellStyle.Default with { Background = new(255, 0, 0), Border = border }),
            [new(2, 1)] = new("", CellStyle.Default, ColumnSpan: 2)
            {
                MergedBorders = [new(new(2, 1), border)],
            },
        };
        var sheet = new ReportSheet("layers", cells, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new());
        var measurer = new CountingMeasurer();
        var plan = new ContinuousLayoutPlan(sheet, measurer);
        Assert.Single(plan.EnumerateLayerCells(measurer, DrawingLayer.Background));
        Assert.Single(plan.EnumerateLayerCells(measurer, DrawingLayer.CellBorder));
        Assert.Single(plan.EnumerateLayerCells(measurer, DrawingLayer.MergedBorder));
        Assert.Equal(0, measurer.Measured);
        Assert.Single(plan.EnumerateLayerCells(measurer, DrawingLayer.Text));
        Assert.Equal(1, measurer.Measured);
        var complete = plan.EnumerateCells(measurer).ToArray();
        Assert.Equal(2, complete.Length);
        Assert.Single(complete[1].MergedBorders!);
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.EnumerateLayerCells(measurer, (DrawingLayer)100));
    }

    [Fact]
    public void Continuous_lazy_layers_match_materialized_canvas_pixels()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("overflow text", CellStyle.Default with { Font = new("Noto Sans JP", 18), Background = new(255, 0, 0) }),
            [new(1, 2)] = new("B", CellStyle.Default with { Font = new("Noto Sans JP", 12), Background = new(0, 255, 0) }),
        };
        var border = new BorderStyle(Top: new(2, new(0, 0, 255)));
        cells.Add(new(2, 1), new("", CellStyle.Default, ColumnSpan: 2) { MergedBorders = [new(new(2, 1), border)] });
        using var image = new global::SkiaSharp.SKBitmap(2, 2);
        image.Erase(global::SkiaSharp.SKColors.Blue);
        using var encoded = image.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
        var sheet = new ReportSheet("canvas", cells, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(200, 200))
        {
            Images = [new(new(1, 1), 10, 10, 30, 30, encoded.ToArray(), ZIndex: 1)],
            Shapes =
            [
                new(new(1, 1), 5, 5, 30, 30, ShapeKind.Rectangle, new(new(255, 255, 0), null), null, 0, 0),
                new(new(1, 1), 20, 20, 30, 30, ShapeKind.Rectangle, new(new(255, 0, 255), null), null, 0, 2),
            ],
        };
        using var fonts = new ConversionFontResources();
        var manager = new FontManager(Request(OutputFormat.Png).FontOptions);
        var measurer = new PdfSharpTextMeasurer(manager, cacheLayouts: true);
        var plan = new ContinuousLayoutPlan(sheet, measurer);
        var materialized = new ReportLayoutEngine(measurer).LayoutContinuous(sheet);
        using var lazy = new MemoryStream();
        using var full = new MemoryStream();
        var renderer = new PngRenderer(manager);
        renderer.RenderCanvas(new DrawCommandGeneratorPass().GenerateContinuous(plan, measurer), plan.Width, plan.Height, lazy);
        renderer.RenderCanvas(new DrawCommandGeneratorPass().Generate(materialized.Document), materialized.Width, materialized.Height, full);
        using var lazyBitmap = global::SkiaSharp.SKBitmap.Decode(lazy.ToArray());
        using var fullBitmap = global::SkiaSharp.SKBitmap.Decode(full.ToArray());
        Assert.Equal(fullBitmap.Pixels, lazyBitmap.Pixels);
        using var lazySvg = new MemoryStream();
        using var fullSvg = new MemoryStream();
        var svg = new SvgRenderer(manager);
        svg.RenderCanvas(new DrawCommandGeneratorPass().GenerateContinuous(plan, measurer), plan.Width, plan.Height, lazySvg);
        svg.RenderCanvas(new DrawCommandGeneratorPass().Generate(materialized.Document), materialized.Width, materialized.Height, fullSvg);
        Assert.Equal(System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(fullSvg.ToArray())).ToString(),
            System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(lazySvg.ToArray())).ToString());
    }

    [Fact]
    public void Svg_spill_matches_memory_output_and_cleans_up_after_sink_failure()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var commands = Enumerable.Range(0, 100).Select(index => (DrawCommand)new FillRectangleCommand(1,
                new(index, index, 20, 20), new(255, 0, 0))).ToArray();
            using var memory = new MemoryStream();
            using var spilled = new MemoryStream();
            var renderer = new SvgRenderer();
            renderer.RenderCanvas(commands, 200, 200, memory);
            renderer.RenderCanvas(commands, 200, 200, spilled, new() { MemoryThresholdBytes = 1, TemporaryDirectory = directory.FullName });
            Assert.Equal(System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(memory.ToArray())).ToString(),
                System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(spilled.ToArray())).ToString());
            Assert.Empty(directory.GetFiles());
            Assert.Throws<IOException>(() => renderer.RenderCanvas(commands, 200, 200, new FailingOutput(),
                new() { MemoryThresholdBytes = 1, TemporaryDirectory = directory.FullName }));
            Assert.Empty(directory.GetFiles());
            Assert.Throws<InvalidDataException>(() => renderer.RenderCanvas(commands, 200, 200, Stream.Null,
                new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = false }));
            Assert.Empty(directory.GetFiles());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Scanned_fonts_load_bytes_once_on_selection_and_detect_changed_files()
    {
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "font.ttf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"), path);
        var loaded = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "fontsBytesLoaded")
            {
                loaded += value;
            }
        };
        try
        {
            var options = new FontOptions { AllowSystemFonts = false, UseFontPack = false, FontDirectories = [directory.FullName] };
            var manager = new FontManager(options);
            Assert.Equal(0, loaded);
            var first = manager.Resolve(new("Noto Sans JP"));
            Assert.Equal(new FileInfo(path).Length, loaded);
            var second = manager.Resolve(new("Noto Sans JP", 700));
            Assert.Same(first.FontData, second.FontData);
            Assert.Equal(new FileInfo(path).Length, loaded);
            var changed = new FontManager(options);
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Throws<InvalidOperationException>(() => changed.Resolve(new("Noto Sans JP")));
            Assert.Same(first.FontData, manager.Resolve(new("Noto Sans JP")).FontData);
        }
        finally
        {
            ConversionMetrics.Observer = null;
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Reader_projection_preserves_original_indices_names_and_unselected_geometry()
    {
        using var book = new XLWorkbook();
        var first = book.AddWorksheet("Ignored");
        first.Cell("C5").Value = "unselected body";
        first.Range("C5:D6").Merge();
        first.Row(2).Height = 37;
        var selected = book.AddWorksheet("Selected");
        selected.Cell("A1").Value = "selected body";
        using var input = new MemoryStream();
        book.SaveAs(input);
        input.Position = 0;
        using var owner = await WorkbookInputPreparer.ReadAsync(input, new(), default);
        var document = new ExcelRenderer.Excel.ExcelReader().Read(owner, null, ["Selected"]);
        Assert.Equal(new[] { "Ignored", "Selected" }, document.Sheets.Select(sheet => sheet.Name));
        Assert.Empty(document.Sheets[0].Cells);
        Assert.Single(document.Sheets[0].MergedRanges);
        Assert.Equal(37, document.Sheets[0].Rows[2].Height);
        Assert.Equal(2, document.Sheets[1].SourceSheetIndex);
        Assert.Single(document.Sheets[1].Cells);
        var metadata = new WorkbookRenderMetadata(document);
        Assert.All(metadata.Sheets, sheet => Assert.Empty(sheet.Cells));
        Assert.Equal(new SheetGeometry(document.Sheets[0]).RowStart(5), metadata.Geometry("Ignored").RowStart(5));
    }

    [Fact]
    public async Task Nonseekable_spool_respects_limits_cancellation_and_cleanup()
    {
        var directory = Directory.CreateTempSubdirectory();
        using var zipBytes = new MemoryStream();
        using (var archive = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("test.xml").Open();
            entry.Write(new byte[1000]);
        }

        var bytes = zipBytes.ToArray();
        var options = new WorkbookInputOptions { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory.FullName };
        try
        {
            using var input = new NonseekableInput(new MemoryStream(bytes));
            using (var owner = await WorkbookInputPreparer.ReadAsync(input, options, default))
            {
                using var read = owner.OpenRead();
                Assert.Equal(bytes.Length, read.Length);
            }

            Assert.True(input.CanRead);
            foreach (var failure in new[] { options with { MaxInputBytes = 2 }, options with { MaxUncompressedZipBytes = 2 } })
            {
                using var rejected = new NonseekableInput(new MemoryStream(bytes));
                await Assert.ThrowsAsync<InvalidDataException>(() => WorkbookInputPreparer.ReadAsync(rejected, failure, default));
                Assert.Empty(directory.GetFiles());
            }

            using var canceled = new NonseekableInput(new MemoryStream(bytes));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkbookInputPreparer.ReadAsync(canceled, options, new(true)));
            Assert.Empty(directory.GetFiles());
            using var invalid = new NonseekableInput(new MemoryStream(new byte[100]));
            await Assert.ThrowsAsync<InvalidDataException>(() => WorkbookInputPreparer.ReadAsync(invalid, options, default));
            Assert.Empty(directory.GetFiles());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Png_sink_failure_is_rethrown_in_managed_code_and_releases_owned_resources()
    {
        var commands = new DrawCommand[] { new FillRectangleCommand(1, new(0, 0, 20, 20), new(255, 0, 0)) };
        Assert.Throws<IOException>(() => new PngRenderer().RenderCanvas(commands, 20, 20, new FailingOutput()));
        Assert.Null(ImageResources.Current);
        Assert.Null(ConversionFontResources.Current);
    }

    [Fact]
    public void Explicit_corrupt_font_fails_before_drawing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Throws<InvalidDataException>(() => new FontManager(new FontOptions
            {
                UseFontPack = false,
                AllowSystemFonts = false,
                Registrations = [new("corrupt", path)],
            }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pagination_plans_measure_no_text_and_materialize_only_current_page_candidates()
    {
        var cells = Enumerable.Range(1, 1000).ToDictionary(row => new CellAddress(row, 1), row => new ReportCell("unique " + row, CellStyle.Default));
        var sheet = new ReportSheet("pages", cells, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(100, 100, 0, 0, 0, 0));
        var measurer = new CountingMeasurer();
        var plan = Assert.Single(new ReportLayoutEngine(measurer).Plan(sheet));
        Assert.Equal(0, measurer.Measured);
        Assert.True(plan.Pages.Count > 100);
        var first = plan.Build(0, 1, plan.Pages.Count, measurer);
        Assert.InRange(first.Cells.Count, 1, 10);
        Assert.Equal(first.Cells.Count, measurer.Measured);
        var last = plan.Build(plan.Pages.Count - 1, plan.Pages.Count, plan.Pages.Count, measurer);
        Assert.Equal(first.Cells.Count + last.Cells.Count, measurer.Measured);
    }

    [Fact]
    public void Svg_xml_copy_cancellation_cleans_spill_and_leaves_output_open()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var cancel = new CancellationTokenSource();
            using var output = new CancelOnWrite(cancel);
            var commands = Enumerable.Range(0, 2000).Select(index => (DrawCommand)new FillRectangleCommand(1, new(index, 0, 1, 1), new(255, 0, 0)));
            Assert.Throws<OperationCanceledException>(() => new SvgRenderer().RenderCanvas(commands, 2000, 20, output, new()
            {
                MemoryThresholdBytes = 1,
                TemporaryDirectory = directory,
            }, cancel.Token));
            Assert.Empty(Directory.GetFiles(directory));
            Assert.True(output.CanWrite);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Retired_image_owner_keeps_active_native_handle_until_lease_release()
    {
        using var outer = new ImageResources();
        using var bitmap = new global::SkiaSharp.SKBitmap(2, 2);
        using var encoded = bitmap.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
        var bytes = encoded.ToArray();
        var owner = new ImageResources();
        using var lease = owner.Acquire(bytes, () => (global::SkiaSharp.SKImage.FromEncodedData(bytes), 16L));
        Assert.NotNull(lease);
        var image = lease.Value;
        owner.Dispose();
        Assert.Same(outer, ImageResources.Current);
        Assert.NotEqual(IntPtr.Zero, image.Handle);
        lease.Dispose();
        Assert.Equal(IntPtr.Zero, image.Handle);
    }

    [Fact]
    public void Header_footer_clock_snapshot_replays_date_time_and_original_page_count()
    {
        var clock = new DateTime(2020, 10, 12, 13, 14, 0);
        var sheet = new ReportSheet("Clock", new Dictionary<CellAddress, ReportCell>(), new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new())
        {
            HeaderFooter = new(new("&D &T &P/&N", "", ""), new("", "", "")),
        };
        var first = Assert.Single(HeaderFooterLayout.Create(sheet, 3, 12, clock));
        var replay = Assert.Single(HeaderFooterLayout.Create(sheet, 3, 12, clock));
        Assert.Equal(clock.ToShortDateString() + " " + clock.ToShortTimeString() + " 3/12", first.Text);
        Assert.Equal(first, replay);
    }

    [Fact]
    public void Planned_object_geometry_is_shared_across_distant_page_builds()
    {
        var image = new ReportImage(new(1, 1), 0, 0, 20, 15000, [1], Name: "shared");
        var images = new TracedImageList(image);
        var cells = Enumerable.Range(1, 1000).ToDictionary(row => new CellAddress(row, 1), row => new ReportCell("row " + row, CellStyle.Default));
        var sheet = new ReportSheet("objects", cells, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(100, 100, 0, 0, 0, 0), new(new(1, 1), new(1000, 1)), Images: images);
        var measurer = new CountingMeasurer();
        var plan = Assert.Single(new ReportLayoutEngine(measurer).Plan(sheet));
        Assert.Equal(1, images.Enumerations);
        var first = plan.Build(0, 1, plan.Pages.Count, measurer);
        var last = plan.Build(plan.Pages.Count - 1, plan.Pages.Count, plan.Pages.Count, measurer);
        Assert.Equal(1, images.Enumerations);
        Assert.Same(image.ImageBytes, Assert.Single(first.Images!).ImageBytes);
        Assert.Same(image.ImageBytes, Assert.Single(last.Images!).ImageBytes);
    }

    private sealed class TracedImageList(ReportImage image) : IReadOnlyList<ReportImage>
    {
        public int Count => 1;

        internal int Enumerations { get; private set; }

        public ReportImage this[int index] => index == 0 ? image : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<ReportImage> GetEnumerator()
        {
            Enumerations++;
            yield return image;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CancelOnWrite(CancellationTokenSource cancel) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            cancel.Cancel();
        }
    }

    private sealed class CountingMeasurer : ExcelRenderer.Abstractions.ITextMeasurer
    {
        internal int Measured { get; private set; }

        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap)
        {
            Measured++;
            return new(10, 10);
        }
    }

    private sealed class NonseekableInput(Stream source) : Stream
    {
        public override bool CanRead => source.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => source.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingOutput : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("sink failure");
    }

    private static RenderRequest Request(OutputFormat format) => new()
    {
        OutputFormat = format,
        Dpi = 24,
        FontOptions = new()
        {
            AllowSystemFonts = false,
            UseFontPack = false,
            Registrations = [new("Noto Sans JP", Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"))],
            FallbackFamilies = ["Noto Sans JP"],
        },
    };

    private sealed class DisposableImage : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class CapturedPageSink : IRenderOutputSink
    {
        internal List<MemoryStream> Outputs { get; } = [];

        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
        {
            var stream = new MemoryStream();
            Outputs.Add(stream);
            return new(stream);
        }

        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;

        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
    }

    private sealed class ObservedSink : IRenderOutputSink
    {
        internal int OpenCount { get; private set; }

        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
        {
            OpenCount++;
            return new(Stream.Null);
        }

        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;

        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
    }
}
