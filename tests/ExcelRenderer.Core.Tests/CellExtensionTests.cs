using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class CellExtensionTests
{
    [Theory]
    [InlineData(false, 39, .5)]
    [InlineData(true, 31, 3.5)]
    public void Generic_insets_are_identical_in_measurement_and_final_content(bool custom, double expectedWidth, double expectedLeft)
    {
        var cell = new ReportCell("sample", CellStyle.Default);
        var sheet = new ReportSheet("sheet", new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = cell }, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new());
        var measurer = new Measurer();
        var context = new ReportLayoutContext(sheet, measurer)
        {
            Policy = custom ? new InsetsPolicy() : new CoreLayoutPolicy(),
            ColumnLayouts = new() { [1] = new(1, 0, 40) },
            RowLayouts = new() { [1] = new(1, 0, 20) },
        };
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);
        Assert.Equal(expectedWidth, measurer.Width);
        Assert.Equal(expectedWidth, context.CellLayouts[new(1, 1)].ContentBounds.Width);
        Assert.Equal(expectedLeft, context.CellLayouts[new(1, 1)].ContentBounds.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_decoration_stage_precedes_borders_and_can_suppress_only_text(bool custom)
    {
        var style = CellStyle.Default with { Background = new(255, 0, 0), Border = new(Bottom: new()) };
        var cells = new[]
        {
            new RenderCell(new("first", style), new(0, 0, 40, 15)) { SourceAddress = new(1, 1), TextLayout = new(new(1, 1), []) },
            new RenderCell(new("second", style), new(0, 20, 40, 15)) { SourceAddress = new(2, 1), TextLayout = new(new(1, 1), []) },
        };
        var commands = new DrawCommandGeneratorPass().GeneratePage(new(1, cells), null, custom ? new Extension() : null);
        Assert.All(commands.Take(2), command => Assert.Equal(new ReportColor(255, 0, 0), Assert.IsType<FillRectangleCommand>(command).Color));
        Assert.Equal(custom ? 1 : 2, commands.OfType<DrawTextCommand>().Count());
        Assert.Contains(commands.OfType<DrawTextCommand>(), command => command.Text == "second");
        if (custom)
        {
            Assert.All(commands.Skip(2).Take(2), command => Assert.Equal(new ReportColor(0, 255, 0), Assert.IsType<FillRectangleCommand>(command).Color));
            Assert.IsType<DrawLineCommand>(commands[4]);
            Assert.Equal(new ReportColor(0, 0, 255), Assert.IsType<FillRectangleCommand>(commands.Last()).Color);
        }
        else
        {
            Assert.Equal(2, commands.OfType<FillRectangleCommand>().Count());
        }
    }

    private sealed class InsetsPolicy : CoreLayoutPolicy
    {
        internal override (double Left, double Right) GetCellContentInsets(ReportCell cell, double ordinaryContentWidth) => (3, 5);
    }

    private sealed class Measurer : ITextMeasurer
    {
        internal double Width { get; private set; }

        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap)
        {
            Width = availableWidth;
            return new(availableWidth, 10);
        }
    }

    private sealed class Extension : ICoreDrawingExtension
    {
        public bool RequiresTextLayout => false;

        public IEnumerable<DrawCommand> CreateCellDecorationCommands(int pageNumber, RenderCell cell) => [new FillRectangleCommand(pageNumber, cell.Bounds, new(0, 255, 0))];

        public bool ShouldDrawCellText(RenderCell cell) => cell.SourceAddress?.Row != 1;

        public IEnumerable<DrawCommand> CreateObjectCommands(RenderPage page) => [new FillRectangleCommand(page.Number, new(0, 0, 1, 1), new(0, 0, 255))];
    }
}
