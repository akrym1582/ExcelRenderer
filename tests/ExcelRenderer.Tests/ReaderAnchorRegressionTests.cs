using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class ReaderAnchorRegressionTests
{
    [Fact]
    public void Reader_preserves_drawingml_anchor_coordinates()
    {
        WithSheet(sheet =>
        {
            Assert.Equal(3, sheet.Images!.Count);
            var absolute = sheet.Images.Single(i => i.Name == "absolute").DrawingAnchor!;
            Assert.Equal(DrawingAnchorKind.Absolute, absolute.Kind);
            Assert.Null(absolute.From); Assert.Null(absolute.To); Assert.Null(absolute.EditAs);
            Near(15, absolute.PositionX); Near(30, absolute.PositionY);
            Near(45, absolute.ExtentWidth); Near(60, absolute.ExtentHeight);
            Near(0, absolute.FromOffsetX); Near(0, absolute.FromOffsetY); Near(0, absolute.ToOffsetX); Near(0, absolute.ToOffsetY);
            var one = sheet.Images.Single(i => i.Name == "one").DrawingAnchor!;
            Assert.Equal(DrawingAnchorKind.OneCell, one.Kind);
            Assert.Equal(new CellAddress(4, 3), one.From); Assert.Null(one.To); Assert.Null(one.EditAs);
            Near(7.5, one.FromOffsetX); Near(15, one.FromOffsetY);
            Near(30, one.ExtentWidth); Near(22.5, one.ExtentHeight);
            Near(0, one.PositionX); Near(0, one.PositionY); Near(0, one.ToOffsetX); Near(0, one.ToOffsetY);
            var two = sheet.Images.Single(i => i.Name == "two").DrawingAnchor!;
            Assert.Equal(DrawingAnchorKind.TwoCell, two.Kind);
            Assert.Equal(new CellAddress(4, 3), two.From); Assert.Equal(new CellAddress(6, 5), two.To);
            Near(7.5, two.FromOffsetX); Near(15, two.FromOffsetY); Near(3.75, two.ToOffsetX); Near(7.5, two.ToOffsetY);
            Near(0, two.PositionX); Near(0, two.PositionY); Near(0, two.ExtentWidth); Near(0, two.ExtentHeight);
            Assert.Equal("oneCell", two.EditAs);
        });
    }

    [Theory]
    [InlineData(false, 1d)]
    [InlineData(false, 0.5d)]
    [InlineData(true, 1d)]
    [InlineData(true, 0.5d)]
    public void Reader_anchors_reach_draw_commands_with_print_origin_and_scale(bool shifted, double scale)
    {
        WithSheet(read =>
        {
            var originRow = shifted ? 4 : 1;
            var originColumn = shifted ? 3 : 1;
            var sheet = read with
            {
                PrintArea = new(new(originRow, originColumn), new(10, 8)), PrintAreas = [],
                PageSettings = new(1000, 1000, 20, 25, 30, 35, Scale: scale),
            };
            // ColumnDefinition.Width is already points, calculated by Reader from Excel character units.
            // Independently sum those values, never interpret the fixture's 1.25 character units as points.
            double ColumnStart(int c) => Enumerable.Range(1, c - 1).Sum(i => sheet.Columns[i].Width);
            double RowStart(int r) => Enumerable.Range(1, r - 1).Sum(i => (double)(8 + i));
            for (var row = 1; row <= 10; row++) { Near(8 + row, sheet.Rows[row].Height); }
            var x = ColumnStart(3) + 7.5; var y = RowStart(4) + 15;
            var rectangles = new Dictionary<string, ReportRect>
            {
                ["absolute"] = new(15, 30, 45, 60),
                ["one"] = new(x, y, 30, 22.5),
                ["two"] = new(x, y, ColumnStart(5) + 3.75 - x, RowStart(6) + 7.5 - y),
            };
            var engine = new ReportLayoutEngine(new PdfSharpTextMeasurer(new OutputFixture.FixedManager()));
            var document = engine.Layout(sheet);
            var page = Assert.Single(document.Pages);
            Assert.Equal(3, page.Images!.Count);
            var commands = new DrawCommandGeneratorPass().Generate(document).OfType<DrawImageCommand>().ToArray();
            Assert.Equal(3, commands.Length);
            foreach (var image in sheet.Images!)
            {
                var expected = rectangles[image.Name!];
                var placed = page.Images.Single(i => i.ImageBytes.SequenceEqual(image.ImageBytes));
                var command = commands.Single(c => c.ImageBytes.SequenceEqual(image.ImageBytes));
                // sheet -> layout subtracts the C4 origin, then page applies the scale exactly once and margins once.
                var pageExpected = new ReportRect(20 + (expected.X - ColumnStart(originColumn)) * scale,
                    25 + (expected.Y - RowStart(originRow)) * scale, expected.Width * scale, expected.Height * scale);
                Rect(pageExpected, placed.Bounds); Rect(pageExpected, command.Bounds);
                // Reverse the page placement to prove the unscaled sheet-space rectangle in the normal layout path.
                Rect(expected, new((placed.Bounds.X - 20) / scale + ColumnStart(originColumn),
                    (placed.Bounds.Y - 25) / scale + RowStart(originRow), placed.Bounds.Width / scale, placed.Bounds.Height / scale));
            }
        });
    }

    private static void WithSheet(Action<ReportSheet> action)
    {
        var path = DrawingAnchorWorkbookFixture.Create();
        try { action(Assert.Single(new ExcelReader(new OutputFixture.FixedManager()).Read(path).Sheets)); }
        finally { File.Delete(path); }
    }

    private static void Rect(ReportRect expected, ReportRect actual)
    {
        Near(expected.X, actual.X); Near(expected.Y, actual.Y); Near(expected.Width, actual.Width); Near(expected.Height, actual.Height);
    }
    private static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 0.01, $"Expected {expected}, got {actual}");
}
