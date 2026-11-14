using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class AutomaticPrintAreaTests
{
    /// <summary>Automatic printing keeps leading whitespace, while explicit areas and canvases retain their origins.</summary>
    [Theory]
    [InlineData(false, false, 1, 1)]
    [InlineData(true, false, 4, 3)]
    [InlineData(false, true, 4, 3)]
    [InlineData(true, true, 4, 3)]
    public void Leading_blank_rows_and_columns_are_preserved_only_for_automatic_printing(bool explicitArea, bool continuous, int firstRow, int firstColumn)
    {
        var used = new CellRange(new(4, 3), new(5, 5));
        var sheet = new ReportSheet(
            "Sheet1",
            new Dictionary<CellAddress, ReportCell> { [new(4, 3)] = new("merged", CellStyle.Default, RowSpan: 2, ColumnSpan: 3) },
            new Dictionary<int, ColumnDefinition>(),
            new Dictionary<int, RowDefinition>(),
            [used],
            new(),
            PrintArea: explicitArea ? used : null);
        var context = new ReportLayoutContext(sheet, new ExcelRenderer.SkiaSharp.SkiaTextMeasurer());

        new ResolvePrintAreaPass { IgnoreExplicitPrintArea = continuous }.Execute(context);

        Assert.Equal(new CellRange(new(firstRow, firstColumn), used.Last), context.PrintArea);

        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        Assert.Equal(firstColumn == 1 ? 128 : 0, context.ColumnLayouts[3].X);
        Assert.Equal(firstRow == 1 ? 45 : 0, context.RowLayouts[4].Y);
    }
    /// <summary>Whole-axis print areas keep the specified axis and bound the unspecified axis to content.</summary>
    [Theory]
    [InlineData(1, 3, 1048576, 26, 5, 26)]
    [InlineData(3, 1, 20, 16384, 20, 5)]
    [InlineData(1, 1, 1048576, 16384, 5, 5)]
    [InlineData(1, 3, 100, 26, 100, 26)]
    public void Whole_axis_print_areas_stop_at_content(int firstRow, int firstColumn, int lastRow, int lastColumn, int expectedRow, int expectedColumn)
    {
        var sheet = new ReportSheet(
            "Sheet1",
            new Dictionary<CellAddress, ReportCell> { [new(4, 3)] = new("merged", CellStyle.Default, RowSpan: 2, ColumnSpan: 3) },
            new Dictionary<int, ColumnDefinition>(),
            new Dictionary<int, RowDefinition>(),
            [new(new(4, 3), new(5, 5))],
            new(),
            PrintArea: new(new(firstRow, firstColumn), new(lastRow, lastColumn)));
        var context = new ReportLayoutContext(sheet, new ExcelRenderer.SkiaSharp.SkiaTextMeasurer());

        new ResolvePrintAreaPass().Execute(context);
        new HiddenRowColumnPass().Execute(context);

        Assert.Equal(new CellRange(new(firstRow, firstColumn), new(expectedRow, expectedColumn)), context.PrintArea);
        Assert.Equal(expectedRow - firstRow + 1, context.VisibleRows.Count);
        Assert.Equal(expectedColumn - firstColumn + 1, context.VisibleColumns.Count);
    }

    /// <summary>An empty whole-column area resolves to one row instead of a million blank rows.</summary>
    [Fact]
    public void Empty_whole_column_area_is_bounded()
    {
        var sheet = new ReportSheet(
            "Empty",
            new Dictionary<CellAddress, ReportCell>(),
            new Dictionary<int, ColumnDefinition>(),
            new Dictionary<int, RowDefinition>(),
            [],
            new(),
            PrintArea: new(new(1, 1), new(1048576, 26)));
        var context = new ReportLayoutContext(sheet, new ExcelRenderer.SkiaSharp.SkiaTextMeasurer());

        new ResolvePrintAreaPass().Execute(context);

        Assert.Equal(new CellRange(new(1, 1), new(1, 26)), context.PrintArea);
    }

    /// <summary>Caller-created legacy models can use zero-based addresses without losing their first cell.</summary>
    [Fact]
    public void Automatic_range_preserves_zero_based_model_addresses()
    {
        var sheet = new ReportSheet(
            "Legacy",
            new Dictionary<CellAddress, ReportCell> { [new(0, 0)] = new("first", CellStyle.Default) },
            new Dictionary<int, ColumnDefinition>(),
            new Dictionary<int, RowDefinition>(),
            [],
            new());
        var context = new ReportLayoutContext(sheet, new ExcelRenderer.SkiaSharp.SkiaTextMeasurer());

        new ResolvePrintAreaPass().Execute(context);

        Assert.Equal(new CellRange(new(0, 0), new(0, 0)), context.PrintArea);
    }

}
