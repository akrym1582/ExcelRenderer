using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Checks saved XML styles, page placement and all visual output paths.</summary>
public sealed class SharedBorderWorkbookTests
{
    [Fact]
    public void Saved_different_border_and_style_ids_resolve_identically()
    {
        using var input = Fixture();
        var sheet = new ExcelReader().Read(input).Sheets[0];
        var commands = PageCommands(sheet);
        var lines = Flatten(commands).OfType<DrawLineCommand>().ToArray();
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.Equal(BorderLineStyle.Double, line.Style.LineStyle));
        var streamed = new DrawCommandGeneratorPass().GenerateContinuous(new(sheet, new EmptyMeasurer()), new EmptyMeasurer()).ToArray();
        Assert.Equal(3, Flatten(streamed).OfType<DrawLineCommand>().Count());
    }

    [Fact]
    public void Conditional_clear_uses_final_style_and_keeps_the_opposing_thin_line()
    {
        using var input = Fixture();
        var sheet = new ExcelReader().Read(input).Sheets[4];
        Assert.Null(sheet.Cells[new(1, 1)].Style.Border?.Right);
        var line = Assert.Single(Flatten(PageCommands(sheet)).OfType<DrawLineCommand>());
        Assert.Equal(0.5, line.Style.Width);
        input.Position = 0;
        var original = new ExcelReader().Read(input, new() { ApplyConditionalFormatting = false }).Sheets[4];
        Assert.Equal(2, Assert.Single(Flatten(PageCommands(original)).OfType<DrawLineCommand>()).Style.Width);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conditional_clear_on_a_saved_outside_owner_is_not_undone_by_range_selection(bool selectedPage)
    {
        using var input = Fixture();
        var sheet = new ExcelReader().Read(input, new()
        {
            GetSelectedRange = _ => (1, 2, 1, 2),
            IsConditionalFormattingTarget = selectedPage ? (_, row, column) => row == 1 && column == 2 : null,
        }).Sheets[4];
        Assert.Null(sheet.Cells[new(1, 1)].Style.Border?.Right);
        sheet = sheet with { RequestedRange = new(new(1, 2), new(1, 2)), PrintArea = new(new(1, 2), new(1, 2)) };
        var line = Assert.Single(Flatten(PageCommands(sheet)).OfType<DrawLineCommand>());
        Assert.Equal(0.5, line.Style.Width);
    }

    [Fact]
    public void Saved_merged_partial_edges_are_not_filled_in()
    {
        using var input = Fixture();
        var sheet = new ExcelReader().Read(input).Sheets[2];
        var leader = Assert.Single(sheet.Cells).Value;
        Assert.Null(leader.Style.Border);
        Assert.Equal(3, leader.MergedBorders!.Count);
        var lines = Flatten(PageCommands(sheet)).OfType<DrawLineCommand>().ToArray();
        Assert.Equal(5, lines.Length);
        Assert.Single(lines, line => line.Style.LineStyle == BorderLineStyle.Double);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selection_keeps_only_the_external_neighbors_edge(bool continuous)
    {
        var blank = new ReportCell(null, CellStyle.Default);
        var outside = new ReportCell("outside", CellStyle.Default with { Background = new(255, 0, 0), Border = new(Right: new(0.75, LineStyle: BorderLineStyle.Double)) });
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell> { [new(2, 2)] = blank }) with
        {
            RequestedRange = new(new(2, 2), new(2, 2)),
            PrintArea = new(new(2, 2), new(2, 2)),
            BorderSourceCells = new Dictionary<CellAddress, ReportCell> { [new(2, 1)] = outside, [new(2, 2)] = blank },
        };
        var commands = continuous
            ? new DrawCommandGeneratorPass().GenerateContinuous(new(sheet, new EmptyMeasurer()), new EmptyMeasurer()).ToArray()
            : PageCommands(sheet);
        var line = Assert.IsType<DrawLineCommand>(Assert.Single(Flatten(commands)));
        Assert.Equal(BorderLineStyle.Double, line.Style.LineStyle);
        if (continuous)
        {
            var materialized = new ReportLayoutEngine(new EmptyMeasurer()).LayoutContinuous(sheet);
            var materializedCommands = new DrawCommandGeneratorPass().Generate(materialized.Document);
            Assert.Equal(Flatten(commands), Flatten(materializedCommands));
            Assert.Equal(commands.OfType<DrawViewportCommand>().Select(viewport => viewport.Clip), materializedCommands.OfType<DrawViewportCommand>().Select(viewport => viewport.Clip));
        }
    }

    [Fact]
    public void Empty_printed_cell_can_get_a_border_from_outside_the_print_area()
    {
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell>
        {
            [new(2, 1)] = new("outside", CellStyle.Default with { Border = new(Right: new()) }),
        }) with { PrintArea = new(new(2, 2), new(2, 2)) };
        var line = Assert.IsType<DrawLineCommand>(Assert.Single(Flatten(PageCommands(sheet))));
        Assert.Equal(line.X1, line.X2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hidden_axis_does_not_make_its_own_borders_visible(bool rows)
    {
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new(null, CellStyle.Default with { Border = rows ? new(Bottom: new()) : new(Right: new()) }),
            [rows ? new(2, 1) : new(1, 2)] = new(null, CellStyle.Default with { Border = rows ? new(Top: new(5), Bottom: new(5)) : new(Left: new(5), Right: new(5)) }),
            [rows ? new(3, 1) : new(1, 3)] = new(null, CellStyle.Default with { Border = rows ? new(Top: new(0.75, LineStyle: BorderLineStyle.Double)) : new(Left: new(0.75, LineStyle: BorderLineStyle.Double)) }),
        }) with
        {
            Columns = rows ? new Dictionary<int, ColumnDefinition>() : new Dictionary<int, ColumnDefinition> { [2] = new(20, true) },
            Rows = rows ? new Dictionary<int, RowDefinition> { [2] = new(20, true) } : new Dictionary<int, RowDefinition>(),
        };
        var line = Assert.IsType<DrawLineCommand>(Assert.Single(Flatten(PageCommands(sheet))));
        Assert.Equal(BorderLineStyle.Double, line.Style.LineStyle);
    }

    [Fact]
    public void Repeated_titles_and_multiple_areas_do_not_duplicate_page_edges()
    {
        var cells = Enumerable.Range(1, 8).ToDictionary(row => new CellAddress(row, 1), row => new ReportCell(null, CellStyle.Default with { Border = new(Right: new(0.75, LineStyle: BorderLineStyle.Double), Bottom: new()) }));
        var sheet = Sheet(cells) with
        {
            DefaultRowHeight = 20,
            PrintAreas = [new(new(1, 1), new(4, 1)), new(new(5, 1), new(8, 1))],
            PageSettings = new(100, 65, MarginLeft: 5, MarginRight: 5, MarginTop: 5, MarginBottom: 5, TitleRows: new(1, 1)),
        };
        var document = new ReportLayoutEngine(new EmptyMeasurer()).Layout(sheet);
        Assert.True(document.Pages.Count > 2);
        foreach (var page in document.Pages)
        {
            var lines = Flatten(new DrawCommandGeneratorPass().Generate(new([page]))).OfType<DrawLineCommand>();
            Assert.All(lines.GroupBy(line => (line.X1, line.Y1, line.X2, line.Y2)), group => Assert.Single(group));
        }
    }

    [Fact]
    public void Page_cut_inside_a_cell_does_not_create_a_border_at_the_cut()
    {
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new(null, CellStyle.Default with { Border = new(Bottom: new()) }),
            [new(2, 1)] = new(null, CellStyle.Default with { Border = new(Bottom: new()) }),
            [new(3, 1)] = new(null, CellStyle.Default with { Border = new(Bottom: new()) }),
        }) with { DefaultRowHeight = 30, PageSettings = new(100, 45, MarginLeft: 0, MarginRight: 0, MarginTop: 0, MarginBottom: 0) };
        var page = new ReportLayoutEngine(new EmptyMeasurer()).Layout(sheet).Pages[0];
        Assert.DoesNotContain(Flatten(new DrawCommandGeneratorPass().Generate(new([page]))).OfType<DrawLineCommand>(), line => line.Y1 == 45 && line.Y2 == 45);
    }

    [Fact]
    public void Saved_row_and_column_default_styles_remain_border_candidates()
    {
        using var input = Fixture();
        var sheet = new ExcelReader().Read(input).Sheets[3];
        Assert.NotEmpty(Flatten(PageCommands(sheet)).OfType<DrawLineCommand>());
    }

    [Fact]
    public void Continuous_canvas_includes_both_strokes_of_outer_double_borders()
    {
        var side = new BorderSide(0.75, LineStyle: BorderLineStyle.Double);
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = new(null, CellStyle.Default with { Border = new(side, side, side, side) }) });
        var layout = new ReportLayoutEngine(new EmptyMeasurer()).LayoutContinuous(sheet);
        Assert.Equal(66.25, layout.Width);
        Assert.Equal(17.25, layout.Height);
        var cell = Assert.Single(layout.Document.Pages[0].Cells);
        Assert.Equal(1.125, cell.Bounds.X);
        Assert.Equal(1.125, cell.Bounds.Y);
        var commands = new DrawCommandGeneratorPass().Generate(layout.Document);
        Assert.Equal(4, commands.Count);
    }

    [Fact]
    public void Shared_lines_reach_PDF_PNG_SVG_and_PostScript()
    {
        var doubleSide = new BorderSide(0.75, LineStyle: BorderLineStyle.Double);
        var page = new RenderPage(1,
        [new(new(null, CellStyle.Default with { Border = new(Right: doubleSide) }), new(10, 10, 30, 30)), new(new(null, CellStyle.Default with { Border = new(Left: doubleSide) }), new(40, 10, 30, 30))]);
        var commands = new DrawCommandGeneratorPass().Generate(new([page]));
        var settings = new PageSettings(80, 80);
        using var pdf = new MemoryStream();
        using var png = new MemoryStream();
        using var svg = new MemoryStream();
        using var ps = new MemoryStream();
        new Pdf.PdfRenderer().Render(commands, settings, pdf);
        new PngRenderer().RenderPage(commands, settings, png, 288);
        new SvgRenderer().RenderPage(commands, settings, svg);
        new PostScript.PostScriptRenderer().Render(commands, settings, ps);
        using var serialized = new Svg.Skia.SKSvg();
        svg.Position = 0;
        var picture = serialized.Load(svg)!;
        using var bitmap = new SKBitmap(320, 320);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(320 / picture.CullRect.Width, 320 / picture.CullRect.Height);
            canvas.DrawPicture(picture);
        }

        Assert.True(bitmap.GetPixel(157, 100).Red < 180);
        Assert.True(bitmap.GetPixel(163, 100).Red < 180);
        Assert.Equal(SKColors.White, bitmap.GetPixel(160, 100));
        if (Environment.GetEnvironmentVariable("BORDER_VALIDATION_DIR") is { } directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var (extension, output) in new[] { ("pdf", pdf), ("png", png), ("svg", svg), ("ps", ps) })
            {
                File.WriteAllBytes(Path.Combine(directory, "shared-double." + extension), output.ToArray());
            }
        }
    }

    private static MemoryStream Fixture() => new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SampleInputs", "BorderResolution", "border-conflicts.xlsx")));

    private static IReadOnlyList<DrawCommand> PageCommands(ReportSheet sheet) => new DrawCommandGeneratorPass().Generate(new ReportLayoutEngine(new EmptyMeasurer()).Layout(sheet));

    private static ReportSheet Sheet(IReadOnlyDictionary<CellAddress, ReportCell> cells) => new("borders", cells, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(200, 200));

    private static IEnumerable<DrawCommand> Flatten(IEnumerable<DrawCommand> commands) => commands.SelectMany(command => command is DrawViewportCommand viewport ? Flatten(viewport.Commands) : new[] { command });

    private sealed class EmptyMeasurer : ITextMeasurer
    {
        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(1, 1);
    }
}
