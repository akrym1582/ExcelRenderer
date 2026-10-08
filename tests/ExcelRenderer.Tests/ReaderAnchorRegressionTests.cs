using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>DrawingML の画像アンカーを読み取り、印刷原点と倍率を反映した最終描画座標を検証します。</summary>
public sealed class ReaderAnchorRegressionTests
{
    /// <summary>保存済み DrawingML の絶対・単一セル・二セルアンカーから、位置・寸法・オフセットと editAs を正しく読み取ることを検証します。</summary>
    [Fact(DisplayName = "保存済み DrawingML の絶対・単一セル・二セルアンカーから、位置・寸法・オフセットと editAs を正しく読み取る")]
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

    /// <summary>印刷範囲の原点と倍率を変更しても、読み取った画像アンカーが余白と倍率を一度だけ適用した描画命令座標に到達することを検証します。</summary>
    /// <param name="shifted">印刷範囲の原点を C4 に移動するかどうか。</param>
    /// <param name="scale">ページ座標と寸法に適用する倍率。</param>
    [Theory(DisplayName = "印刷範囲の原点と倍率を変更しても、読み取った画像アンカーが余白と倍率を一度だけ適用した描画命令座標に到達する")]
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

    /// <summary>画像アンカーの一時ブックを読み取って検証処理に渡し、最後にファイルを削除します。</summary>
    /// <param name="action">読み取ったシートに対して実行する検証処理。</param>
    private static void WithSheet(Action<ReportSheet> action)
    {
        var path = DrawingAnchorWorkbookFixture.Create();
        try { action(Assert.Single(new ExcelReader(new OutputFixture.FixedManager()).Read(path).Sheets)); }
        finally { File.Delete(path); }
    }

    /// <summary>矩形の X・Y 座標と幅・高さを、それぞれ期待値と許容誤差内で比較します。</summary>
    /// <param name="expected">判定または数値比較の期待値。</param>
    /// <param name="actual">比較対象の実際値。</param>
    private static void Rect(ReportRect expected, ReportRect actual)
    {
        Near(expected.X, actual.X); Near(expected.Y, actual.Y); Near(expected.Width, actual.Width); Near(expected.Height, actual.Height);
    }

    /// <summary>座標の実際値が期待値と許容誤差内で一致することを検証します。</summary>
    /// <param name="expected">判定または数値比較の期待値。</param>
    /// <param name="actual">比較対象の実際値。</param>
    private static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 0.01, $"Expected {expected}, got {actual}");
}
