using ReportEngine.Abstractions;
using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;
using ReportEngine.PdfSharp;
using Xunit;

namespace ReportEngine.Tests;

public sealed class LayoutPassTests
{
    [Fact]
    public void ColumnLayoutPass_assigns_cumulative_positions()
    {
        var context = CreateContext(columns: new Dictionary<int, ColumnDefinition>
        {
            [1] = new(80), [2] = new(120), [3] = new(60)
        });
        context.PrintArea = new(new(1, 1), new(1, 3));
        new HiddenRowColumnPass().Execute(context);

        new ColumnLayoutPass().Execute(context);

        Assert.Equal(0, context.ColumnLayouts[1].X);
        Assert.Equal(80, context.ColumnLayouts[2].X);
        Assert.Equal(200, context.ColumnLayouts[3].X);
    }

    [Fact]
    public void CellBoundsPass_uses_merged_cell_span()
    {
        var address = new CellAddress(1, 1);
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell> { [address] = new("title", CellStyle.Default, 2, 2) },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(80), [2] = new(120) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(20), [2] = new(25) });
        context.PrintArea = new(address, new(2, 2));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);

        new CellBoundsPass().Execute(context);

        Assert.Equal(new ReportRect(0, 0, 200, 45), context.CellLayouts[address].Bounds);
    }

    [Fact]
    public void PaginationPass_does_not_split_rows()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("one", CellStyle.Default),
            [new(2, 1)] = new("two", CellStyle.Default)
        };
        var context = CreateContext(cells: cells,
            rows: new Dictionary<int, RowDefinition> { [1] = new(40), [2] = new(40) },
            pageSettings: new(100, 90, 10, 10, 10, 10));
        context.PrintArea = new(new(1, 1), new(2, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(2, context.RenderDocument!.Pages.Count);
        Assert.All(context.RenderDocument.Pages, page => Assert.Single(page.Cells));
    }

    [Fact]
    public void DrawCommandGenerator_orders_fill_before_border_before_text()
    {
        var style = CellStyle.Default with
        {
            Background = new(1, 2, 3),
            Border = new(new BorderSide())
        };
        var document = new RenderDocument(
        [
            new RenderPage(1, [new(new("text", style), new(0, 0, 10, 10))])
        ]);

        var commands = new DrawCommandGeneratorPass().Generate(document);

        Assert.Collection(commands,
            command => Assert.IsType<FillRectangleCommand>(command),
            command => Assert.IsType<DrawBorderCommand>(command),
            command => Assert.IsType<DrawTextCommand>(command));
    }

    [Fact]
    public void PdfSharpRenderer_writes_a_pdf_document()
    {
        using var output = new MemoryStream();

        new PdfSharpRenderer().Render([], new PageSettings(), output);

        Assert.True(output.Length > 0);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.GetBuffer(), 0, 5));
    }

    private static ReportLayoutContext CreateContext(
        IReadOnlyDictionary<CellAddress, ReportCell>? cells = null,
        IReadOnlyDictionary<int, ColumnDefinition>? columns = null,
        IReadOnlyDictionary<int, RowDefinition>? rows = null,
        PageSettings? pageSettings = null) =>
        new(new ReportSheet("Sheet1",
            cells ?? new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = new(null, CellStyle.Default) },
            columns ?? new Dictionary<int, ColumnDefinition> { [1] = new() },
            rows ?? new Dictionary<int, RowDefinition> { [1] = new() },
            [], pageSettings ?? new()), new FixedTextMeasurer());

    private sealed class FixedTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(10, 10);
    }
}
