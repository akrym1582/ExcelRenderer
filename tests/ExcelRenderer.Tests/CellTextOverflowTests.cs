using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using ExcelRenderer.PostScript;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Svg.Skia;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class CellTextOverflowTests
{
    [Theory]
    [InlineData(HorizontalAlignment.Left, false)]
    [InlineData(HorizontalAlignment.Left, true)]
    [InlineData(HorizontalAlignment.Right, false)]
    [InlineData(HorizontalAlignment.Right, true)]
    [InlineData(HorizontalAlignment.Center, false)]
    [InlineData(HorizontalAlignment.Center, true)]
    public void Paged_and_continuous_outputs_paint_blank_cells_but_never_occupied_cells(HorizontalAlignment alignment, bool continuous)
    {
        var sheet = Sheet(alignment);
        var manager = new OutputFixture.FixedManager();
        var measurer = new SkiaTextMeasurer(manager);
        var engine = new ReportLayoutEngine(measurer);
        var commands = continuous
            ? new DrawCommandGeneratorPass().GenerateContinuous(new(sheet, measurer), measurer).ToArray()
            : new DrawCommandGeneratorPass().Generate(engine.Layout(sheet));
        var source = Assert.Single(commands.OfType<DrawViewportCommand>(), viewport => viewport.Commands.OfType<DrawTextCommand>().Any(text => text.Text.StartsWith("WW")));
        var command = Assert.IsType<DrawTextCommand>(Assert.Single(source.Commands));
        Assert.Equal(40.5, command.Bounds.X);
        Assert.Equal(19, command.Bounds.Width);
        Assert.Equal(alignment == HorizontalAlignment.Left ? 40 : 20, source.Clip.X);
        Assert.Equal(alignment == HorizontalAlignment.Right ? 60 : 80, source.Clip.X + source.Clip.Width);
        var materialized = new DrawCommandGeneratorPass().Generate(continuous ? engine.LayoutContinuous(sheet).Document : engine.Layout(sheet));
        Assert.Equal(commands.OfType<DrawViewportCommand>().Select(viewport => viewport.Clip), materialized.OfType<DrawViewportCommand>().Select(viewport => viewport.Clip));

        using var output = new MemoryStream();
        new PngRenderer(manager).RenderPage(commands, sheet.PageSettings, output, 72);
        using var png = SKBitmap.Decode(output.ToArray());
        AssertNoRed(png, 0, 20);
        AssertNoRed(png, 80, 100);
        if (alignment != HorizontalAlignment.Left) AssertRed(png, 20, 40);
        if (alignment != HorizontalAlignment.Right) AssertRed(png, 60, 80);

        using var svgOutput = new MemoryStream();
        new SvgRenderer(manager).RenderPage(commands, sheet.PageSettings, svgOutput);
        var xml = Encoding.UTF8.GetString(svgOutput.ToArray());
        Assert.Contains("clipPath", xml);
        using var svg = new SKSvg();
        svgOutput.Position = 0;
        var picture = svg.Load(svgOutput)!;
        using var raster = new SKBitmap(100, 60);
        using (var canvas = new SKCanvas(raster))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(100 / picture.CullRect.Width, 60 / picture.CullRect.Height);
            canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);
            canvas.DrawPicture(picture);
        }

        AssertNoRed(raster, 0, 20);
        AssertNoRed(raster, 80, 100);
        if (alignment != HorizontalAlignment.Left) AssertRed(raster, 20, 40);
        if (alignment != HorizontalAlignment.Right) AssertRed(raster, 60, 80);

        using var pdfOutput = new MemoryStream();
        new PdfRenderer(manager).Render(commands, sheet.PageSettings, pdfOutput);
        Assert.Contains("W", OutputFixture.Content(pdfOutput.ToArray()));
        using var psOutput = new MemoryStream();
        new PostScriptRenderer(manager).Render(commands, sheet.PageSettings, psOutput);
        Assert.Contains("clip", Encoding.ASCII.GetString(psOutput.ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Center_alignment_anchor_does_not_move_when_only_one_side_is_blocked(bool left)
    {
        var original = Sheet(HorizontalAlignment.Center);
        var cells = original.Cells.Where(pair => pair.Key.Column != (left ? 5 : 1)).ToDictionary(pair => pair.Key, pair => pair.Value);
        var sheet = original with { Cells = cells };
        var measurer = new SkiaTextMeasurer(new OutputFixture.FixedManager());
        var source = new ReportLayoutEngine(measurer).Layout(sheet).Pages[0].Cells.Single(cell => cell.SourceAddress == new CellAddress(2, 3));
        var positions = TextLayoutPlacement.Place(source.TextLayout!, source.ContentBounds, HorizontalAlignment.Center, source.Cell.Style.VerticalAlignment);
        Assert.Equal(50, positions[0].Left + (source.TextLayout!.Lines[0].Width / 2), 6);
        Assert.Equal(left ? 20 : 0, source.TextClipBounds!.Value.X);
        Assert.Equal(left ? 100 : 80, source.TextClipBounds.Value.X + source.TextClipBounds.Value.Width);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_range_clips_at_the_selected_edge_and_keeps_canvas_size(bool continuous)
    {
        var sheet = Sheet(HorizontalAlignment.Center) with { RequestedRange = new(new(2, 2), new(2, 4)), PrintArea = new(new(2, 2), new(2, 4)) };
        var engine = new ReportLayoutEngine(new SkiaTextMeasurer(new OutputFixture.FixedManager()));
        var result = engine.LayoutContinuous(sheet);
        Assert.Equal(60, result.Width);
        Assert.Equal(20, result.Height);
        var commands = new DrawCommandGeneratorPass().Generate(continuous ? result.Document : engine.Layout(sheet));
        var viewport = Assert.Single(commands.OfType<DrawViewportCommand>());
        Assert.Equal(60, viewport.Clip.Width);
        var text = Assert.IsType<DrawTextCommand>(Assert.Single(viewport.Commands));
        Assert.Equal(20.5, text.Bounds.X);
    }

    [Fact]
    public void Trim_bounds_use_the_clipped_visible_ink()
    {
        var sheet = Sheet(HorizontalAlignment.Left);
        var manager = new OutputFixture.FixedManager();
        var commands = new DrawCommandGeneratorPass().Generate(new ReportLayoutEngine(new SkiaTextMeasurer(manager)).Layout(sheet));
        var source = Assert.Single(commands.OfType<DrawViewportCommand>());
        var visible = DrawCommandBounds.Get([source], new(0, 0, 100, 60), manager)!.Value;
        var unbounded = DrawCommandBounds.Get(source.Commands, new(0, 0, 100, 60), manager)!.Value;
        Assert.True(visible.X + visible.Width <= 80);
        Assert.True(unbounded.X + unbounded.Width > 80);
    }

    [Theory]
    [InlineData(FormulaDisplayMode.CachedValue)]
    [InlineData(FormulaDisplayMode.Recalculate)]
    public void Reader_and_model_copy_preserve_blank_string_and_formula_occupancy(FormulaDisplayMode mode)
    {
        using var input = Workbook();
        var sheet = new ExcelReader().Read(input, new() { FormulaDisplayMode = mode }).Sheets[0];
        Assert.False(sheet.Cells[new(1, 2)].HasStoredValue);
        for (var column = 3; column <= 7; column++) Assert.True(sheet.Cells[new(1, column)].HasStoredValue);
        // Force a public model copy rather than borrowing the reader's neutral dictionary.
        sheet = sheet with { Cells = sheet.Cells.ToDictionary(pair => pair.Key, pair => pair.Value with { Style = pair.Value.Style with { Background = new(255, 255, 255) } }) };
        var page = new ReportLayoutEngine(new SkiaTextMeasurer(new OutputFixture.FixedManager())).Layout(sheet).Pages[0];
        var source = page.Cells.Single(cell => cell.SourceAddress == new CellAddress(1, 1));
        Assert.Equal(page.Cells.Single(cell => cell.SourceAddress == new CellAddress(1, 3)).Bounds.X, source.TextClipBounds!.Value.X + source.TextClipBounds.Value.Width, 6);
    }

    [Theory]
    [InlineData(OutputFormat.Pdf, ImageLayoutMode.Paginated)]
    [InlineData(OutputFormat.Png, ImageLayoutMode.Paginated)]
    [InlineData(OutputFormat.Png, ImageLayoutMode.Continuous)]
    [InlineData(OutputFormat.Svg, ImageLayoutMode.Paginated)]
    [InlineData(OutputFormat.Svg, ImageLayoutMode.Continuous)]
    [InlineData(OutputFormat.PostScript, ImageLayoutMode.Paginated)]
    public async Task Common_xlsx_reaches_each_exporter_with_text_clipping(OutputFormat format, ImageLayoutMode layout)
    {
        using var input = Workbook();
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = format,
            ImageLayout = layout,
            FontOptions = new() { AllowSystemFonts = false },
            Trim = new() { Enabled = true },
        }, new SingleStreamOutputSink(output));
        Assert.True(output.Length > 100);
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "CellTextOverflow");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"main-{layout}.{format.ToString().ToLowerInvariant()}"), output.ToArray());
        if (format == OutputFormat.Svg) Assert.Contains("clipPath", Encoding.UTF8.GetString(output.ToArray()));
        if (format == OutputFormat.PostScript) Assert.Contains("clip", Encoding.ASCII.GetString(output.ToArray()));
    }

    [Fact]
    public async Task Same_workbook_reaches_Core_PDF_and_preserves_main_links()
    {
        using var input = Workbook();
        using var core = new MemoryStream();
        await Core.ExcelConverter.ConvertAsync(input, core, new() { FontFilePath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf") });
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "CellTextOverflow");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "core.pdf"), core.ToArray());
        input.Position = 0;
        using var main = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Pdf, FontOptions = new() { AllowSystemFonts = false } }, new SingleStreamOutputSink(main));
        File.WriteAllBytes(Path.Combine(directory, "main-untrimmed.pdf"), main.ToArray());
        input.Position = 0;
        File.WriteAllBytes(Path.Combine(directory, "common.xlsx"), ((MemoryStream)input).ToArray());
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(main.ToArray());
        Assert.Single(pdf.GetPage(1).GetHyperlinks());
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left)]
    [InlineData(HorizontalAlignment.Right)]
    [InlineData(HorizontalAlignment.Center)]
    public void Adjacent_blockers_stop_at_source_cell_edges(HorizontalAlignment alignment)
    {
        var original = Sheet(alignment);
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(2, 2)] = original.Cells[new(2, 1)],
            [new(2, 3)] = original.Cells[new(2, 3)],
            [new(2, 4)] = original.Cells[new(2, 5)],
        };
        var sheet = original with { Cells = cells };
        var manager = new OutputFixture.FixedManager();
        var commands = new DrawCommandGeneratorPass().Generate(new ReportLayoutEngine(new SkiaTextMeasurer(manager)).Layout(sheet));
        var source = Assert.Single(commands.OfType<DrawViewportCommand>());
        Assert.Equal(40, source.Clip.X);
        Assert.Equal(20, source.Clip.Width);
        using var output = new MemoryStream();
        new PngRenderer(manager).RenderPage(commands, sheet.PageSettings, output, 72);
        using var bitmap = SKBitmap.Decode(output.ToArray());
        AssertNoRed(bitmap, 0, 40);
        AssertNoRed(bitmap, 60, 100);
        AssertRed(bitmap, 40, 60);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Merged_text_overflows_blank_cells_but_is_cropped_before_the_neighbor(bool continuous)
    {
        var original = Sheet(HorizontalAlignment.Left);
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(2, 1)] = original.Cells[new(2, 1)],
            [new(2, 3)] = original.Cells[new(2, 3)] with { ColumnSpan = 2 },
            [new(2, 6)] = original.Cells[new(2, 5)],
        };
        var sheet = original with
        {
            Cells = cells,
            MergedRanges = [new(new(2, 3), new(2, 4))],
            PageSettings = new(120, 60, 0, 0, 0, 0),
            PrintArea = new(new(1, 1), new(3, 6)),
        };
        var manager = new OutputFixture.FixedManager();
        var measurer = new SkiaTextMeasurer(manager);
        var commands = continuous
            ? new DrawCommandGeneratorPass().GenerateContinuous(new(sheet, measurer), measurer).ToArray()
            : new DrawCommandGeneratorPass().Generate(new ReportLayoutEngine(measurer).Layout(sheet));
        using var output = new MemoryStream();
        new PngRenderer(manager).RenderPage(commands, sheet.PageSettings, output, 72);
        using var bitmap = SKBitmap.Decode(output.ToArray());

        AssertRed(bitmap, 80, 100);
        AssertNoRed(bitmap, 100, 120);
        var viewport = Assert.Single(commands.OfType<DrawViewportCommand>());
        Assert.Equal(60, viewport.Clip.Width);
    }

    private static ReportSheet Sheet(HorizontalAlignment alignment)
    {
        var font = new FontStyle("Noto Sans JP", 12, Color: new(255, 0, 0));
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(2, 1)] = new("L", CellStyle.Default with { Font = font with { Color = new(0, 0, 255) } }),
            [new(2, 3)] = new(new string('W', 30), CellStyle.Default with { Font = font, HorizontalAlignment = alignment }),
            [new(2, 5)] = new("R", CellStyle.Default with { Font = font with { Color = new(0, 0, 255) } }),
        };
        return new("overflow", cells, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(100, 60, 0, 0, 0, 0), new(new(1, 1), new(3, 5)))
        { DefaultColumnWidth = 20, DefaultRowHeight = 20 };
    }

    private static MemoryStream Workbook()
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbook = doc.AddWorkbookPart();
            var sheet = workbook.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new S.Worksheet("""
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cols><col min="1" max="7" width="5" customWidth="1"/></cols><sheetData><row r="1" ht="25" customHeight="1">
                <c r="A1" s="1" t="inlineStr"><is><t>WWWWWWWWWWWWWWWWWWWWWWWWWWWWWW</t></is></c><c r="B1" s="1"/>
                <c r="C1" t="inlineStr"><is><t/></is></c><c r="D1" t="str"><v/></c><c r="E1" t="str"><f>""</f><v/></c>
                <c r="F1"><f t="shared" si="0" ref="F1:G1">1</f><v>1</v></c><c r="G1"><f t="shared" si="0"/></c>
                </row></sheetData></worksheet>
                """);
            var link = sheet.AddHyperlinkRelationship(new Uri("https://example.com/overflow"), true);
            sheet.Worksheet.Append(new S.Hyperlinks(new S.Hyperlink { Reference = "A1", Id = link.Id }));
            workbook.Workbook = new S.Workbook(new S.Sheets(new S.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "overflow" }));
            workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = new S.Stylesheet(
                new S.Fonts(new S.Font(new S.FontSize { Val = 12 }, new S.FontName { Val = "Noto Sans JP" }), new S.Font(new S.FontSize { Val = 12 }, new S.FontName { Val = "Noto Sans JP" }, new S.Color { Rgb = "FFFF0000" })),
                new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
                new S.Borders(new S.Border()), new S.CellStyleFormats(new S.CellFormat()),
                new S.CellFormats(new S.CellFormat(), new S.CellFormat { FontId = 1, ApplyFont = true }),
                new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
        }

        stream.Position = 0;
        return stream;
    }

    private static void AssertNoRed(SKBitmap bitmap, int left, int right) => Assert.False(RedPixels(bitmap, left, right));
    private static void AssertRed(SKBitmap bitmap, int left, int right) => Assert.True(RedPixels(bitmap, left, right));
    private static bool RedPixels(SKBitmap bitmap, int left, int right) => Enumerable.Range(0, bitmap.Height).Any(y => Enumerable.Range(left, right - left).Any(x => bitmap.GetPixel(x, y) is { Red: > 180, Green: < 100, Blue: < 100 }));
}
