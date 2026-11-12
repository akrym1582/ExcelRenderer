using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class TextOverflowTests
{
    [Theory]
    [InlineData((int)HorizontalAlignment.Left, 0, 3, 20, 60)]
    [InlineData((int)HorizontalAlignment.Right, 1, 0, 20, 40)]
    [InlineData((int)HorizontalAlignment.Center, 1, 3, 20, 60)]
    [InlineData((int)HorizontalAlignment.Center, 0, 3, 0, 60)]
    [InlineData((int)HorizontalAlignment.Center, 1, 0, 20, 120)]
    [InlineData((int)HorizontalAlignment.Center, 0, 0, 0, 120)]
    public void Alignment_uses_independent_blockers_and_keeps_original_bounds(int alignmentValue, int left, int right, double expectedLeft, double expectedRight)
    {
        var alignment = (HorizontalAlignment)alignmentValue;
        var cells = new Dictionary<CellAddress, ReportCell> { [new(1, 2)] = new(new string('W', 40), CellStyle.Default with { HorizontalAlignment = alignment }) };
        if (left != 0) cells[new(1, left)] = new("block", CellStyle.Default);
        if (right != 0) cells[new(1, right + 1)] = new("block", CellStyle.Default);
        var sheet = Sheet(cells);
        var page = new ReportLayoutEngine(new Measurer()).Plan(sheet)[0].Build(0, 1, 1, new Measurer());
        var cell = page.Cells.Single(cell => cell.SourceAddress == new CellAddress(1, 2));
        Assert.Equal(new ReportRect(20, 0, 20, 10), cell.Bounds);
        Assert.Equal(expectedLeft, cell.TextClipBounds!.Value.X);
        Assert.Equal(expectedRight, cell.TextClipBounds.Value.X + cell.TextClipBounds.Value.Width);
        Assert.Equal(100, cell.TextClipBounds.Value.Height); // Not the source row's 10pt height.
        var text = Assert.Single(new DrawCommandGeneratorPass().GeneratePage(page, null, new LegacyDrawing()), command => command is DrawTextCommand text && text.SourceAddress == new CellAddress(1, 2));
        Assert.Equal(cell.TextClipBounds, text.ClipBounds);
        Assert.Equal(cell.ContentBounds, ((DrawTextCommand)text).Bounds);
    }

    [Fact]
    public void Sparse_index_skips_hidden_values_and_blocks_every_row_of_a_merge()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(2, 2)] = new("source", CellStyle.Default),
            [new(2, 3)] = new("hidden", CellStyle.Default),
            [new(2, 4)] = new(string.Empty, CellStyle.Default) { HasStoredValue = false },
            [new(2, 16000)] = new("distant", CellStyle.Default),
        };
        var sheet = Sheet(cells) with
        {
            Columns = new Dictionary<int, ColumnDefinition> { [3] = new(20, true) },
            MergedRanges = [new(new(1, 5), new(1000000, 8))],
        };
        var geometry = new SheetGeometry(sheet);
        var index = geometry.GetTextOverflow(sheet);
        Assert.Same(index, geometry.GetTextOverflow(sheet));
        Assert.Equal((double.NegativeInfinity, 60d), index.Find(new(2, 2))!.Value);
        Assert.Equal((140d, 319960d), index.Find(new(2, 9))!.Value);
        Assert.Equal((140d, double.PositiveInfinity), index.Find(new(999999, 9))!.Value);
        Assert.Null(index.Find(new(1, 5)));
    }

    [Theory]
    [InlineData("wrap")]
    [InlineData("shrink")]
    [InlineData("rotation")]
    [InlineData("vertical")]
    [InlineData("special")]
    [InlineData("merge")]
    [InlineData("short")]
    public void Existing_text_modes_and_short_text_do_not_gain_a_clip(string mode)
    {
        var style = CellStyle.Default with { WrapText = mode == "wrap", ShrinkToFit = mode == "shrink", TextRotation = mode == "rotation" ? 30 : 0, TopToBottom = mode == "vertical" };
        var cell = new ReportCell(mode == "short" ? "a" : new string('W', 40), style, ColumnSpan: mode == "merge" ? 2 : 1) { SuppressTextOverflow = mode == "special" };
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell> { [new(1, 2)] = cell, [new(1, 3)] = new("block", CellStyle.Default) });
        var page = new ReportLayoutEngine(new Measurer()).Plan(sheet)[0].Build(0, 1, 1, new Measurer());
        Assert.Null(page.Cells.Single(cell => cell.SourceAddress == new CellAddress(1, 2)).TextClipBounds);
    }

    [Fact]
    public void Clip_intersection_preserves_empty_clips()
    {
        Assert.Null(TextOverflowPass.Intersect(null, null));
        Assert.Equal(new ReportRect(1, 2, 3, 4), TextOverflowPass.Intersect(new(1, 2, 3, 4), null));
        Assert.Equal(new ReportRect(5, 6, 0, 0), TextOverflowPass.Intersect(new(0, 0, 1, 1), new(5, 6, 1, 1)));
    }

    [Fact]
    public async Task Reader_preserves_empty_strings_hidden_values_formulas_and_style_only_blanks()
    {
        var styles = TestWorkbook.Styles();
        styles.CellFormats!.Append(new S.CellFormat { NumberFormatId = 164, ApplyNumberFormat = true });
        styles.InsertAt(new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "\"\"" }), 0);
        // Use raw XML to retain empty v/f elements and a shared-formula follower without a cache.
        var worksheet = new S.Worksheet("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1">
            <c r="A1" s="1"/><c r="B1" t="inlineStr"><is><t/></is></c><c r="C1" t="str"><v/></c>
            <c r="D1" s="1"><v>42</v></c><c r="E1" t="str"><f>""</f><v/></c>
            <c r="F1"><f t="shared" si="0" ref="F1:G1">1</f><v>1</v></c><c r="G1"><f t="shared" si="0"/></c>
            <c r="H1" t="inlineStr"><is><t xml:space="preserve"> </t></is></c>
            </row></sheetData></worksheet>
            """);
        using var input = TestWorkbook.Create(worksheet, styles);
        await TestSupport.WithSheet(input, (sheet, _) =>
        {
            Assert.False(sheet.Cells[new(1, 1)].HasStoredValue);
            for (var column = 2; column <= 8; column++) Assert.True(sheet.Cells[new(1, column)].HasStoredValue);
            Assert.Equal(string.Empty, sheet.Cells[new(1, 4)].Text);
            Assert.Null(sheet.Cells[new(1, 7)].Formula);
            var index = new SheetGeometry(sheet).GetTextOverflow(sheet);
            Assert.Equal(0, index.Find(new(1, 3))!.Value.Left - new SheetGeometry(sheet).ColumnStart(3));
        });
    }

    [Fact]
    public void Print_area_origin_and_page_boundary_are_in_layout_coordinates()
    {
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 4)] = new(new string('W', 40), CellStyle.Default),
            [new(1, 6)] = new("block", CellStyle.Default),
        }) with { PrintArea = new(new(1, 4), new(1, 8)), PageSettings = new(60, 100, 0, 0, 0, 0) };
        var plan = new ReportLayoutEngine(new Measurer()).Plan(sheet)[0];
        var page = plan.Build(0, 1, plan.Pages.Count, new Measurer());
        var source = page.Cells.Single(cell => cell.SourceAddress == new CellAddress(1, 4));
        Assert.Equal(0, source.Bounds.X);
        Assert.Equal(40, source.TextClipBounds!.Value.Width);
        Assert.Equal(2, plan.Pages.Count);
    }

    [Fact]
    public void Titles_and_body_have_separate_overflow_regions_on_first_and_repeated_pages()
    {
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new(new string('W', 40), CellStyle.Default),
            [new(1, 4)] = new(new string('W', 40), CellStyle.Default with { HorizontalAlignment = HorizontalAlignment.Center }),
        }) with { PageSettings = new(80, 100, 0, 0, 0, 0, TitleColumns: new(1, 2)) };
        var plan = new ReportLayoutEngine(new Measurer()).Plan(sheet)[0];
        Assert.True(plan.Pages.Count > 1);
        for (var index = 0; index < plan.Pages.Count; index++)
        {
            var page = plan.Build(index, index + 1, plan.Pages.Count, new Measurer());
            var title = page.Cells.Single(cell => cell.SourceAddress == new CellAddress(1, 1));
            Assert.Equal(40, title.TextClipBounds!.Value.X + title.TextClipBounds.Value.Width);
            foreach (var body in page.Cells.Where(cell => cell.SourceAddress!.Value.Column > 2))
                Assert.True(body.TextClipBounds!.Value.X >= 40);
        }
    }

    [Fact]
    public void Text_that_fits_before_the_blocker_does_not_add_a_viewport()
    {
        var sheet = Sheet(new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("12345", CellStyle.Default),
            [new(1, 6)] = new("x", CellStyle.Default),
        });
        var page = new ReportLayoutEngine(new Measurer()).Plan(sheet)[0].Build(0, 1, 1, new Measurer());
        Assert.All(page.Cells, cell => Assert.Null(cell.TextClipBounds));
    }

    internal static ReportSheet Sheet(IReadOnlyDictionary<CellAddress, ReportCell> cells) => new("overflow", cells,
        new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(120, 100, 0, 0, 0, 0), new(new(1, 1), new(1, 6)))
    { DefaultColumnWidth = 20, DefaultRowHeight = 10 };

    private sealed class Measurer : ITextMeasurer
    {
        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(text.Length * 5, 12);
    }

    private sealed class LegacyDrawing : ExcelRenderer.Core.Extensibility.ICoreDrawingExtension
    {
        public bool RequiresTextLayout => false;
        public IEnumerable<DrawCommand> CreateObjectCommands(RenderPage page) => [];
        public IEnumerable<DrawCommand> CreateTextObjectCommands(RenderPage page) => [];
        public IEnumerable<DrawCommand> CreateCellDecorationCommands(int pageNumber, RenderCell cell) => [];
        public bool ShouldDrawCellText(RenderCell cell) => true;
    }
}
