using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Markdown;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Svg.Skia;
using Xunit;
using CellStyle = ExcelRenderer.Model.CellStyle;

namespace ExcelRenderer.Tests;

public sealed class RangeTrimHyperlinkTests
{
    [Theory]
    [InlineData("b2", null, 2, 2, 2, 2)]
    [InlineData(" $b$2:$f$40 ", null, 2, 2, 40, 6)]
    [InlineData("'売上 2026'!B2:F40", "売上 2026", 2, 2, 40, 6)]
    [InlineData("'O''Brien!Q'!A1", "O'Brien!Q", 1, 1, 1, 1)]
    [InlineData("XFD1048576", null, 1048576, 16384, 1048576, 16384)]
    public void Parser_accepts_only_normalized_A1_rectangles(string text, string? sheet, int row, int column, int lastRow, int lastColumn)
    {
        var range = CellRangeParser.Parse(text, out var name);
        Assert.Equal(sheet, name);
        Assert.Equal(new CellRange(new(row, column), new(lastRow, lastColumn)), range);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A0")]
    [InlineData("A1048577")]
    [InlineData("XFE1")]
    [InlineData("B2:A1")]
    [InlineData("A1, B2")]
    [InlineData("A:A")]
    [InlineData("1:2")]
    [InlineData("Name")]
    [InlineData("R1C1")]
    [InlineData("A 1")]
    [InlineData("Sheet1:Sheet3!A1")]
    [InlineData("[external.xlsx]Sheet1!A1")]
    [InlineData("'O'Brien'!A1")]
    [InlineData("A999999999999999999999")]
    public void Parser_rejects_non_rectangles(string text) => Assert.Throws<ArgumentException>(() => CellRangeParser.Parse(text, out _));

    [Theory]
    [InlineData("https://example.com/日本語?q=a%20b#part", true)]
    [InlineData("HTTPS://example.com/a", true)]
    [InlineData("mailto:test@example.com", true)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/plain,a", false)]
    [InlineData("file:///tmp/a", false)]
    [InlineData("relative.xlsx", false)]
    [InlineData("\\\\server\\share", false)]
    [InlineData("mailto:test@example.com?body=%0D%0Ahello", false)]
    [InlineData("https://example.com/\n", false)]
    public void Uri_policy_does_not_double_encode_or_accept_unsafe_targets(string target, bool accepted)
    {
        var uri = HyperlinkPolicy.External(target, null);
        Assert.Equal(accepted, uri is not null);
        if (uri is not null) { Assert.DoesNotContain("%2520", uri); }
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://example.com\",\"A\"\"B\")", true, "A\"B")]
    [InlineData("_xlfn.hyperlink(\"#'売上 2026'!B2\")", true, null)]
    [InlineData("HYPERLINK(A1,\"x\")", false, null)]
    [InlineData("HYPERLINK(\"x\",\"y\")&A1", false, "y")]
    [InlineData("HYPERLINK(\"x\",\"y\",\"z\")", false, "y")]
    public void Literal_formula_scanner_consumes_the_whole_expression(string formula, bool supported, string? label)
    {
        Assert.Equal(supported, LiteralHyperlinkFormula.TryRead(formula, out _, out var actual));
        Assert.Equal(label, actual);
    }

    [Fact]
    public void Artificial_scene_trim_and_PDF_annotations_have_independent_numeric_expectations()
    {
        DrawCommand[] commands = [new FillRectangleCommand(1, new(40, 30, 100, 60), new(10, 20, 30))];
        var union = DrawCommandBounds.Get(commands, new(0, 0, 200, 150), new OutputFixture.FixedManager());
        Assert.Equal(new ReportRect(40, 30, 100, 60), union);
        var viewport = new PageViewport(200, 150, union!.Value, 2);
        Assert.Equal(104, viewport.Width);
        Assert.Equal(64, viewport.Height);
        Assert.Equal(new ReportRect(12, 12, 20, 10), viewport.Map(new(50, 40, 20, 10)));
        Assert.Equal((139, 86), PngRenderer.GetPixelDimensions(viewport.Width, viewport.Height, 96, 100000));
        var wrapped = viewport.Apply(commands, 1);
        using var png = new MemoryStream();
        new PngRenderer().RenderCanvas([wrapped], 104, 64, png, 72);
        using var bitmap = SKBitmap.Decode(png.ToArray());
        Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(10, 20, 30), bitmap.GetPixel(3, 3));
        using var pdf = new PdfDocument();
        pdf.AddPage().Width = XUnit.FromPoint(104);
        pdf.Pages[0].Height = XUnit.FromPoint(64);
        pdf.AddPage().Width = XUnit.FromPoint(104);
        pdf.Pages[1].Height = XUnit.FromPoint(64);
        PdfHyperlinkWriter.Write(
            pdf,
            1,
            64,
            [new(new(12, 12, 20, 10), "https://example.com/a?x=1&y=2", null, null, null, null, "tip"),
            new(new(12, 12, 20, 10), null, 2, 42, 22, 64, null)]);
        using var saved = new MemoryStream();
        pdf.Save(saved, false);
        saved.Position = 0;
        using var reloaded = PdfReader.Open(saved, PdfDocumentOpenMode.Modify);
        var external = reloaded.Pages[0].Annotations[0];
        Assert.Equal("/Link", external.Elements.GetName("/Subtype"));
        Assert.Equal("[ 12 42 32 52 ]", external.Elements.GetArray("/Rect")!.ToString());
        Assert.Equal("https://example.com/a?x=1&y=2", external.Elements.GetDictionary("/A")!.Elements.GetString("/URI"));
        var destination = reloaded.Pages[0].Annotations[1].Elements.GetArray("/Dest")!;
        Assert.Same(reloaded.Pages[1], ((global::PdfSharp.Pdf.Advanced.PdfReference)destination.Elements[0]).Value);
        Assert.Equal("/XYZ", destination.Elements[1].ToString());
        Assert.Equal(42, double.Parse(destination.Elements[2].ToString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(42, double.Parse(destination.Elements[3].ToString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, ((PdfInteger)destination.Elements[4]).Value);
    }

    [Fact]
    public void Saved_xlsx_reader_keeps_xml_ranges_empty_links_and_literal_displays()
    {
        var bytes = Fixture();
        using var input = new MemoryStream(bytes);
        var document = new ExcelReader().Read(input);
        Assert.Equal(3, document.Sheets.Count);
        var sheet = document.Sheets[0];
        Assert.Contains(sheet.Hyperlinks, link => link.SourceRange == new CellRange(new(4, 2), new(4, 3)));
        Assert.Contains(sheet.Hyperlinks, link => link.SourceRange == new CellRange(new(5, 2), new(5, 2)));
        Assert.Equal("literal label", sheet.Cells[new(6, 2)].Text);
        Assert.Equal("https://example.com/formula", sheet.Hyperlinks.Single(link => link.SourceRange.First == new CellAddress(6, 2)).Target);
    }

    [Fact]
    public async Task Generated_range_replaces_print_area_and_preserves_partial_merge_geometry()
    {
        var bytes = Fixture();
        var request = Request(OutputFormat.Png) with { Selection = new() { SheetNames = ["Sheet1"], Ranges = [new("Sheet1", new(new(2, 2), new(8, 4)))] } };
        var (result, output) = await Render(bytes, request);
        Assert.Contains(result.Diagnostics, d => d.Code == "ClippedMergedCell");
        Assert.Single(result.Pages);
        Assert.Equal(new CellRange(new(2, 2), new(8, 4)), result.Pages[0].RequestedRange);
        using var bitmap = SKBitmap.Decode(output);
        Assert.Equal(result.Pages[0].PixelWidth, bitmap.Width);
        Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
        using var stream = new MemoryStream(bytes);
        var original = new ExcelReader().Read(stream).Sheets[0];
        var explicitSheet = original with { RequestedRange = new(new(2, 2), new(8, 4)), PrintArea = new(new(2, 2), new(8, 4)), PrintAreas = [], PageSettings = original.PageSettings with { TitleRows = null } };
        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(explicitSheet);
        var merged = layout.Pages.SelectMany(p => p.Cells).Single(c => c.SourceAddress == new CellAddress(2, 1));
        Assert.True(merged.Bounds.X < merged.ClipBounds!.Value.X);
        Assert.Equal(original.Columns[1].Width + original.Columns[2].Width + original.Columns[3].Width, merged.Bounds.Width, 6);
        Assert.Equal("merge label", merged.Cell.Text);
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    public async Task Generated_trim_uses_final_dimensions_in_actual_outputs(OutputFormat format)
    {
        var (result, bytes) = await Render(Fixture(), Request(format) with { Trim = new() { Enabled = true }, Selection = new() { SheetNames = ["Sheet1"] } });
        Assert.NotEmpty(bytes);
        Assert.All(result.Pages, page => Assert.True(page.WidthPoints < page.OriginalWidthPoints));
        var page = result.Pages[0];
        if (format == OutputFormat.Pdf)
        {
            using var input = new MemoryStream(bytes);
            using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            Assert.Equal(page.WidthPoints, pdf.Pages[0].Width.Point, 4);
            Assert.Equal(page.HeightPoints, pdf.Pages[0].Height.Point, 4);
            Assert.NotEmpty(pdf.Pages[0].Annotations.Cast<global::PdfSharp.Pdf.Annotations.PdfAnnotation>());
        }
        else if (format == OutputFormat.Png)
        {
            using var bitmap = SKBitmap.Decode(bytes);
            Assert.Equal(page.PixelWidth, bitmap.Width);
            Assert.Equal(page.PixelHeight, bitmap.Height);
        }
        else
        {
            using var input = new MemoryStream(bytes);
            using var svg = new SKSvg();
            var picture = svg.Load(input);
            Assert.NotNull(picture);
            using var raster = new SKBitmap(page.PixelWidth!.Value, page.PixelHeight!.Value);
            using var canvas = new SKCanvas(raster);
            canvas.Clear(SKColors.White);
            canvas.Scale(raster.Width / picture!.CullRect.Width, raster.Height / picture.CullRect.Height);
            canvas.DrawPicture(picture);
            Assert.Contains(Enumerable.Range(0, raster.Width).Select(x => raster.GetPixel(x, raster.Height / 2)), c => c != SKColors.Transparent);
        }
    }

    [Fact]
    public async Task Generated_PDF_destination_references_final_selected_document_page()
    {
        var (result, bytes) = await Render(Fixture(multipleAreas: true), Request(OutputFormat.Pdf) with { Selection = new() { Pages = [1, 4, 5] } });
        using var input = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        Assert.Equal(3, pdf.Pages.Count);
        var destination = pdf.Pages[0].Annotations.Cast<global::PdfSharp.Pdf.Annotations.PdfAnnotation>().Select(a => a.Elements.GetArray("/Dest")).First(a => a is not null)!;
        Assert.Same(pdf.Pages[1], ((global::PdfSharp.Pdf.Advanced.PdfReference)destination.Elements[0]).Value);
        Assert.Equal(2, result.Pages[1].OutputPageNumber);
    }

    [Fact]
    public async Task Markdown_links_anchors_lists_and_HTML_attributes_are_safe_and_None_is_plain()
    {
        var (_, bytes) = await Render(Fixture(), Request(OutputFormat.Markdown));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("href=\"https://example.com/a?x=1&amp;y=2\"", text);
        Assert.Contains("#xl-s2-r2-c2", text);
        Assert.Contains("id=\"xl-s2-r2-c2\"", text);
        Assert.Contains("Link at B4:C4", text);
        Assert.Contains("Link at B5", text);
        Assert.Contains("literal label", text);
        var (result, plain) = await Render(Fixture(), Request(OutputFormat.Markdown) with { Hyperlinks = HyperlinkMode.None });
        Assert.DoesNotContain("<a ", Encoding.UTF8.GetString(plain));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Hyperlink"));
        Assert.Equal(
            "<a href=\"https://example.com/?a=&quot;x&quot;&amp;b=2\">&lt;&amp;&quot;</a>",
            MarkdownHyperlinks.Format("<&\"", "https://example.com/?a=\"x\"&b=2", true, false));
    }

    [Fact]
    public async Task Strict_and_invalid_arguments_do_not_open_the_sink()
    {
        var request = Request(OutputFormat.Pdf) with { Selection = new() { Ranges = [new("Sheet1", new(new(2, 2), new(8, 4)))] }, DiagnosticOptions = new() { StrictMode = true } };
        var sink = new CaptureSink();
        await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(new MemoryStream(Fixture()), request, sink));
        Assert.Equal(0, sink.OpenCount);
        foreach (var invalid in new[]
        {
            Request(OutputFormat.Markdown) with { Trim = new() { Enabled = true } },
            Request(OutputFormat.Pdf) with { Trim = new() { PaddingPoints = double.NaN } },
            Request(OutputFormat.Pdf) with { Hyperlinks = (HyperlinkMode)10 },
            Request(OutputFormat.Pdf) with { Selection = new() { Ranges = [new("Sheet1", new(new(1, 1), new(1000, 1000)))], MaxRangeCells = 10 } },
            Request(OutputFormat.Pdf) with { Selection = new() { Ranges = [new("Sheet1", new(new(1, 1), new(2, 2))), new("Sheet1", new(new(1, 1), new(2, 2)))] } },
            Request(OutputFormat.Pdf) with { Selection = new() { SheetNames = ["Sheet1"], Ranges = [new("売上 2026", new(new(1, 1), new(2, 2)))] } },
        })
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() => ExcelConverter.RenderAsync(new MemoryStream(Fixture()), invalid, sink));
            Assert.Equal(0, sink.OpenCount);
        }
    }

    [Fact]
    public async Task Continuous_range_has_fixed_dimensions_and_unsafe_link_diagnostics_are_not_evaluated_for_images()
    {
        var range = new CellRange(new(3, 2), new(8, 4));
        var request = Request(OutputFormat.Png) with { ImageLayout = ImageLayoutMode.Continuous, Selection = new() { SheetNames = ["Sheet1"], Ranges = [new("Sheet1", range)] } };
        var (result, output) = await Render(Fixture(), request);
        Assert.Single(result.Artifacts);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Hyperlink"));
        using var input = new MemoryStream(Fixture());
        var sheet = new ExcelReader(new FontManager(request.FontOptions)).Read(input).Sheets[0];
        Assert.Equal(sheet.Columns[2].Width + sheet.Columns[3].Width + sheet.Columns[4].Width, result.Pages[0].WidthPoints, 6);
        Assert.Equal(6 * 20, result.Pages[0].HeightPoints);
        using var bitmap = SKBitmap.Decode(output);
        Assert.Equal(result.Pages[0].PixelWidth, bitmap.Width);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Explicit_geometry_and_body_mapping_apply_scale_exactly_once(double scale)
    {
        var merge = new CellRange(new(2, 1), new(2, 3));
        var sheet = new ReportSheet("known", new Dictionary<CellAddress, ReportCell>
        {
            [new(2, 1)] = new("merge", CellStyle.Default, ColumnSpan: 3),
            [new(3, 3)] = new("body", CellStyle.Default),
        }, Enumerable.Range(1, 5).ToDictionary(i => i, i => new ColumnDefinition(20)),
            Enumerable.Range(1, 5).ToDictionary(i => i, i => new RowDefinition(10)), [merge],
            new PageSettings(200, 150, 10, 15, 10, 15, Scale: scale))
        {
            RequestedRange = new(new(2, 2), new(4, 4)), PrintArea = new(new(2, 2), new(4, 4)),
        };
        var page = Assert.Single(new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet).Pages);
        Assert.Contains(page.Cells, cell => cell.SourceAddress == new CellAddress(2, 1));
        var body = page.Cells.Single(c => c.SourceAddress == new CellAddress(3, 3));
        Assert.Equal(10 + (20 * scale), body.Bounds.X, 6);
        Assert.Equal(15 + (10 * scale), body.Bounds.Y, 6);
        Assert.Equal(20 * scale, body.Bounds.Width, 6);
        var region = Assert.Single(page.SourceRegions);
        Assert.Equal(new ReportRect(20, 10, 60, 30), region.SourceBounds);
        Assert.Equal(new ReportRect(10, 15, 60 * scale, 30 * scale), region.PageBounds);
    }

    [Fact]
    public async Task Existing_three_anchor_saved_fixture_is_clipped_without_canvas_expansion()
    {
        var path = DrawingAnchorWorkbookFixture.Create();
        try
        {
            using (var xml = SpreadsheetDocument.Open(path, true))
            {
                var drawing = xml.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.WorksheetDrawing!;
                var transform = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Transform2D>().First();
                transform.Rotation = 1800000;
                transform.HorizontalFlip = true;
                var fill = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Spreadsheet.BlipFill>().First();
                fill.SourceRectangle = new DocumentFormat.OpenXml.Drawing.SourceRectangle { Left = 10000, Right = 20000 };
            }
            using var input = File.OpenRead(path);
            var sheet = new ExcelReader().Read(input).Sheets[0];
            var selected = sheet with { RequestedRange = new(new(4, 3), new(5, 4)), PrintArea = new(new(4, 3), new(5, 4)) };
            var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).LayoutContinuous(selected);
            Assert.Equal(sheet.Columns[3].Width + sheet.Columns[4].Width, layout.Width, 6);
            Assert.Equal(12 + 13, layout.Height);
            var images = Assert.Single(layout.Document.Pages).Images!;
            Assert.NotEmpty(images);
            Assert.All(images, image => Assert.NotNull(image.ClipBounds));
            var (result, png) = await Render(File.ReadAllBytes(path), Request(OutputFormat.Png) with
            {
                ImageLayout = ImageLayoutMode.Continuous,
                Selection = new() { Ranges = [new("Anchors", new(new(4, 3), new(5, 4)))] },
            });
            using var decoded = SKBitmap.Decode(png);
            Assert.Equal(result.Pages[0].PixelWidth, decoded.Width);
            Assert.Equal(result.Pages[0].PixelHeight, decoded.Height);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 5)]
    public async Task Hidden_only_selection_preserves_empty_trimmed_and_continuous_pages(double padding, double dimension)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("hidden");
        sheet.Rows(1, 3).Hide();
        sheet.Columns(1, 3).Width = 10;
        using var saved = new MemoryStream();
        workbook.SaveAs(saved);
        var selection = new SelectionOptions { Ranges = [new("hidden", new(new(1, 1), new(3, 3)))] };
        var (trimmed, _) = await Render(saved.ToArray(), Request(OutputFormat.Pdf) with { Selection = selection, Trim = new() { Enabled = true, PaddingPoints = padding } });
        Assert.Single(trimmed.Pages);
        Assert.Equal(dimension, trimmed.Pages[0].WidthPoints);
        Assert.Equal(dimension, trimmed.Pages[0].HeightPoints);
        Assert.Contains(trimmed.Diagnostics, d => d.Code == "EmptyContent" && d.Severity == DiagnosticSeverity.Info);
        var (continuous, _) = await Render(saved.ToArray(), Request(OutputFormat.Png) with { Selection = selection, ImageLayout = ImageLayoutMode.Continuous });
        Assert.Equal(1, continuous.Pages[0].WidthPoints);
        Assert.Equal(1, continuous.Pages[0].HeightPoints);
    }

    [Fact]
    public void Trim_counts_explicit_white_fills_and_strokes_but_not_empty_styles()
    {
        var fonts = new OutputFixture.FixedManager();
        var page = new ReportRect(0, 0, 200, 150);
        Assert.Null(DrawCommandBounds.Get([new DrawBorderCommand(1, new(40, 30, 100, 60), new())], page, fonts));
        Assert.Equal(new ReportRect(40, 30, 100, 60), DrawCommandBounds.Get([new FillRectangleCommand(1, new(40, 30, 100, 60), new(255, 255, 255))], page, fonts));
        Assert.Equal(new ReportRect(39, 29, 102, 2), DrawCommandBounds.Get([new DrawLineCommand(1, 40, 30, 140, 30, new(2))], page, fonts));
        var callout = new ReportShape(new(1, 1), 0, 0, 100, 50, ShapeKind.WedgeRectangleCallout, new(new(1, 2, 3), new(0, 0, 0), 2), null, 0, 0);
        Assert.Equal(new ReportRect(39, 29, 102, 62), DrawCommandBounds.Get([new DrawShapeCommand(1, new(40, 30, 100, 50), callout)], page, fonts));
    }

    [Fact]
    public async Task Suppressed_hyperlink_failures_still_fail_before_open_and_excluded_sources_are_quiet()
    {
        var sink = new CaptureSink();
        var request = Request(OutputFormat.Pdf) with
        {
            DiagnosticOptions = new() { TreatAsErrors = ["HyperlinkRejected"], SuppressedCodes = ["HyperlinkRejected"] },
        };
        var error = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(new MemoryStream(Fixture()), request, sink));
        Assert.Equal(0, sink.OpenCount);
        Assert.DoesNotContain(error.CollectedDiagnostics, d => d.Code == "HyperlinkRejected");
        var (result, _) = await Render(Fixture(), Request(OutputFormat.Pdf) with
        {
            Selection = new() { SheetNames = ["Sheet1"], Ranges = [new("Sheet1", new(new(4, 2), new(5, 3)))] },
        });
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Hyperlink"));
        var (omitted, _) = await Render(Fixture(), Request(OutputFormat.Pdf) with { Selection = new() { SheetNames = ["Sheet1"] } });
        Assert.Contains(omitted.Diagnostics, d => d.Code == "HyperlinkTargetOmitted");
        Assert.DoesNotContain(omitted.Diagnostics, d => d.Message.Contains("private-report") || d.Message.Contains("example.com"));
    }

    [Fact]
    public async Task Paginated_PNG_pixel_limit_fails_before_open()
    {
        var sink = new CaptureSink();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ExcelConverter.RenderAsync(new MemoryStream(Fixture()), Request(OutputFormat.Png) with { MaxPngPixels = 10 }, sink));
        Assert.Equal(0, sink.OpenCount);
    }

    [Fact]
    public void Viewport_restores_caller_canvas_after_skipping_undecodable_image()
    {
        using var bitmap = new SKBitmap(20, 20);
        using var canvas = new SKCanvas(bitmap);
        canvas.Translate(3, 4);
        canvas.ClipRect(new SKRect(0, 0, 10, 10));
        var savedCount = canvas.SaveCount;
        var matrix = canvas.TotalMatrix;
        var clip = canvas.DeviceClipBounds;
        var viewport = new PageViewport(200, 150, new(40, 30, 100, 60), 2);
        new SkiaDrawingContext(false).Execute(canvas,
            viewport.Apply([new DrawImageCommand(1, new(40, 30, 10, 10), [1, 2])], 1));
        Assert.Equal(savedCount, canvas.SaveCount);
        Assert.Equal(matrix, canvas.TotalMatrix);
        Assert.Equal(clip, canvas.DeviceClipBounds);
    }

    [Fact]
    public async Task Saved_scoped_names_and_overlapping_definitions_are_deferred_until_output()
    {
        var bytes = Fixture();
        using var stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        using (var xml = SpreadsheetDocument.Open(stream, true))
        {
            var workbook = xml.WorkbookPart!;
            workbook.Workbook.DefinedNames ??= new DefinedNames();
            workbook.Workbook.DefinedNames.Append(new DefinedName("'売上 2026'!$C$3") { Name = "Jump" });
            workbook.Workbook.DefinedNames.Append(new DefinedName("'売上 2026'!$B$2") { Name = "Jump", LocalSheetId = 0 });
            workbook.Workbook.DefinedNames.Append(new DefinedName("SUM('Sheet1'!A1:A2)") { Name = "Complex" });
            var part = (WorksheetPart)workbook.GetPartById(workbook.Workbook.Sheets!.Elements<Sheet>().First().Id!);
            var links = part.Worksheet.GetFirstChild<Hyperlinks>()!;
            links.Elements<Hyperlink>().Single(link => link.Reference == "B3").Location = "Jump";
            links.Append(new Hyperlink { Reference = "B3", Location = "A1" });
            links.Append(new Hyperlink { Reference = "D8", Location = "Complex" });
        }
        var document = new ExcelReader().Read(new MemoryStream(stream.ToArray()));
        var link = document.Sheets[0].Hyperlinks.First(link => link.SourceRange.First == new CellAddress(3, 2));
        Assert.True(HyperlinkPolicy.Internal(document.Sheets[0], link.Target, document.Sheets, out var target, out var address, out _));
        Assert.Equal("売上 2026", target!.Name);
        Assert.Equal(new CellAddress(2, 2), address);
        Assert.False(HyperlinkPolicy.Internal(document.Sheets[0], "Missing!Jump", document.Sheets, out _, out _, out var omitted));
        Assert.Equal("HyperlinkTargetOmitted", omitted);
        var (result, _) = await Render(stream.ToArray(), Request(OutputFormat.Markdown));
        Assert.Contains(result.Diagnostics, d => d.Code == "HyperlinkRejected");
        Assert.Contains(result.Diagnostics, d => d.Code == "HyperlinkUnsupported");
    }

    [Fact]
    public async Task Link_only_ranges_do_not_materialize_blank_cells_and_keep_output_links()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Sheet1");
        using var saved = new MemoryStream();
        workbook.SaveAs(saved);
        saved.Position = 0;
        using (var xml = SpreadsheetDocument.Open(saved, true))
        {
            var part = xml.WorkbookPart!.WorksheetParts.Single();
            var relationship = part.AddHyperlinkRelationship(new Uri("https://example.com/blank"), true);
            part.Worksheet.Append(new Hyperlinks(new Hyperlink { Reference = "B2:D4", Id = relationship.Id }));
        }
        var bytes = saved.ToArray();
        var document = new ExcelReader().Read(new MemoryStream(bytes));
        Assert.Empty(document.Sheets.Single().Cells);
        Assert.Single(document.Sheets.Single().Hyperlinks);
        var (_, pdfBytes) = await Render(bytes, Request(OutputFormat.Pdf));
        using var pdf = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.Import);
        Assert.True(pdf.Pages[0].Annotations.Count > 0);
        var (_, markdown) = await Render(bytes, Request(OutputFormat.Markdown));
        Assert.Contains("Link at B2:D4", Encoding.UTF8.GetString(markdown));
    }

    [Fact]
    public async Task Readable_invalid_ref_is_deferred_but_missing_relationship_is_fatal_before_open()
    {
        foreach (var missingRelationship in new[] { false, true })
        {
            using var saved = new MemoryStream();
            saved.Write(Fixture());
            saved.Position = 0;
            using (var xml = SpreadsheetDocument.Open(saved, true))
            {
                var part = xml.WorkbookPart!.WorksheetParts.First();
                part.Worksheet.GetFirstChild<Hyperlinks>()!.Append(missingRelationship
                    ? new Hyperlink { Reference = "D9", Id = "missing-relationship" }
                    : new Hyperlink { Reference = "#REF", Location = "A1" });
            }
            if (missingRelationship)
            {
                var sink = new CaptureSink();
                await Assert.ThrowsAnyAsync<Exception>(() => ExcelConverter.RenderAsync(new MemoryStream(saved.ToArray()), Request(OutputFormat.Pdf), sink));
                Assert.Equal(0, sink.OpenCount);
            }
            else
            {
                var (result, _) = await Render(saved.ToArray(), Request(OutputFormat.Pdf));
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "HyperlinkRejected");
            }
        }
    }

    [Fact]
    public async Task Samples_are_saved_and_serialized_SVG_matches_trimmed_PNG()
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../TestResults/RangeTrim"));
        Directory.CreateDirectory(folder);
        var fixture = Fixture();
        File.WriteAllBytes(Path.Combine(folder, "fixture.xlsx"), fixture);
        byte[]? png = null;
        byte[]? svg = null;
        foreach (var format in new[] { OutputFormat.Pdf, OutputFormat.Png, OutputFormat.Svg, OutputFormat.Markdown })
        {
            var request = Request(format) with { Dpi = 72, Trim = new() { Enabled = format != OutputFormat.Markdown },
                Selection = format is OutputFormat.Png or OutputFormat.Svg ? new() { SheetNames = ["Sheet1"] } : new() };
            var (result, bytes) = await Render(fixture, request);
            var extension = format == OutputFormat.Markdown ? "md" : format.ToString().ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(folder, "sample." + extension), bytes);
            if (format == OutputFormat.Png) { png = bytes; }
            if (format == OutputFormat.Svg) { svg = bytes; }
            using var manifest = File.Create(Path.Combine(folder, extension + "-manifest.json"));
            await ConversionManifest.WriteAsync(manifest, result);
        }
        using var expected = SKBitmap.Decode(png!);
        using var parsed = new SKSvg();
        using var input = new MemoryStream(svg!);
        var picture = parsed.Load(input)!;
        using var raster = new SKBitmap(expected.Width, expected.Height);
        using (var canvas = new SKCanvas(raster))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale((float)(72d / 96));
            canvas.DrawPicture(picture);
        }
        var ink = 0;
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            var a = expected.GetPixel(x, y);
            if (a.Red < 230 || a.Green < 230 || a.Blue < 230)
            {
                ink++;
                var found = false;
                for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    if (x + dx < 0 || x + dx >= raster.Width || y + dy < 0 || y + dy >= raster.Height) { continue; }
                    var b = raster.GetPixel(x + dx, y + dy);
                    found |= b.Red < 240 || b.Green < 240 || b.Blue < 240;
                }
                Assert.True(found, $"Serialized SVG lost ink near {x},{y}");
            }
        }
        Assert.True(ink > 50);
    }

    internal static byte[] Fixture(bool multipleAreas = false)
    {
        using var workbook = new XLWorkbook();
        foreach (var name in new[] { "Sheet1", "売上 2026", "O'Brien!Q" })
        {
            var sheet = workbook.AddWorksheet(name);
            sheet.Style.Font.FontName = "Noto Sans JP";
            sheet.Style.Font.FontSize = 10;
            sheet.Columns(1, 8).Width = 10;
            sheet.Rows(1, 15).Height = 20;
            sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
            sheet.PageSetup.Scale = 100;
            sheet.PageSetup.Margins.Left = .5;
            sheet.PageSetup.Margins.Top = .5;
            sheet.PageSetup.Margins.Right = .5;
            sheet.PageSetup.Margins.Bottom = .5;
            sheet.PageSetup.PrintAreas.Add("A1:F10");
            sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
            sheet.PageSetup.Header.Center.AddText("Header");
            sheet.PageSetup.Footer.Center.AddText("Footer");
            sheet.Row(13).Hide();
            sheet.Column(8).Hide();
            sheet.Cell("B2").Value = "target";
            sheet.Cell("B3").Value = "internal";
            sheet.Cell("B4").Style.Fill.BackgroundColor = XLColor.White;
            sheet.Cell("B5").Style.Border.BottomBorder = XLBorderStyleValues.Double;
            sheet.Cell("B6").FormulaA1 = "HYPERLINK(\"https://example.com/formula\",\"literal label\")";
            sheet.Cell("B7").FormulaA1 = "HYPERLINK(A1,\"dynamic\")";
            sheet.Cell("B8").Value = "<&\"[]()|日本語";
            sheet.Cell("B8").Style.Fill.BackgroundColor = XLColor.LightBlue;
        }
        workbook.Worksheet(1).Range("A2:C2").Merge().Value = "merge label";
        using (var bitmap = new SKBitmap(4, 4))
        {
            bitmap.Erase(SKColors.Orange);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var picture = new MemoryStream(data.ToArray());
            workbook.Worksheet(1).AddPicture(picture, "range-image").MoveTo(workbook.Worksheet(1).Cell("A9")).WithSize(160, 32);
        }
        if (multipleAreas) { workbook.Worksheet(1).PageSetup.PrintAreas.Add("H1:J10"); workbook.Worksheet(3).PageSetup.PrintAreas.Add("H1:J10"); }
        using var saved = new MemoryStream();
        workbook.SaveAs(saved);
        saved.Position = 0;
        using (var xml = SpreadsheetDocument.Open(saved, true))
        {
            var part = xml.WorkbookPart!.WorksheetParts.First();
            var drawing = part.DrawingsPart!.WorksheetDrawing!;
            drawing.Append(new DocumentFormat.OpenXml.Drawing.Spreadsheet.OneCellAnchor(
                "<xdr:oneCellAnchor xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                "<xdr:from><xdr:col>0</xdr:col><xdr:colOff>254000</xdr:colOff><xdr:row>5</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>" +
                "<xdr:ext cx=\"2032000\" cy=\"381000\"/><xdr:sp><xdr:nvSpPr><xdr:cNvPr id=\"20\" name=\"callout\"/><xdr:cNvSpPr/></xdr:nvSpPr>" +
                "<xdr:spPr><a:xfrm rot=\"1800000\" flipH=\"1\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"2032000\" cy=\"381000\"/></a:xfrm>" +
                "<a:prstGeom prst=\"wedgeRectCallout\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"00CC99\"/></a:solidFill>" +
                "<a:ln w=\"12700\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln></xdr:spPr></xdr:sp><xdr:clientData/></xdr:oneCellAnchor>"));
            var links = new Hyperlinks();
            part.Worksheet.Append(links);
            var uri = part.AddHyperlinkRelationship(new Uri("https://example.com/a?x=1&y=2"), true);
            links.Append(new Hyperlink { Reference = "A2", Id = uri.Id, Tooltip = "tip\nline" });
            links.Append(new Hyperlink { Reference = "B3", Location = multipleAreas ? "'O''Brien!Q'!B2" : "'売上 2026'!B2" });
            links.Append(new Hyperlink { Reference = "B4:C4", Id = uri.Id });
            links.Append(new Hyperlink { Reference = "B5", Id = uri.Id });
            var unsafeUri = part.AddHyperlinkRelationship(new Uri("file:///tmp/private-report.xlsx"), true);
            links.Append(new Hyperlink { Reference = "B8", Id = unsafeUri.Id });
            foreach (var cell in part.Worksheet.Descendants<Cell>().Where(c => c.CellFormula is not null)) { cell.CellValue = null; }
        }
        return saved.ToArray();
    }

    private static RenderRequest Request(OutputFormat format) => new() { OutputFormat = format, FontOptions = new() { AllowSystemFonts = false } };

    private static async Task<(ConversionResult Result, byte[] Bytes)> Render(byte[] input, RenderRequest request)
    {
        var sink = new CaptureSink();
        var result = await ExcelConverter.RenderAsync(new MemoryStream(input), request, sink);
        return (result, sink.Stream.ToArray());
    }

    private sealed class CaptureSink : IRenderOutputSink
    {
        internal MemoryStream Stream { get; } = new();
        internal int OpenCount { get; private set; }
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor descriptor, CancellationToken token) { OpenCount++; Stream.SetLength(0); return new(Stream); }
        public ValueTask CompleteAsync(ArtifactDescriptor descriptor, long length, CancellationToken token) => default;
        public ValueTask AbortAsync(ArtifactDescriptor descriptor, Exception error, CancellationToken token) => default;
    }
}
