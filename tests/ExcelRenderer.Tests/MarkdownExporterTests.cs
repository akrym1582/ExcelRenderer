using ExcelRenderer.Markdown;
using ExcelRenderer.Model;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// シートの視覚レイアウトを Markdown の読み順と表構造へ変換する処理を検証します。
/// </summary>
public sealed class MarkdownExporterTests
{
    /// <summary>
    /// 結合セル、数式表示値および外部ファイル化した画像が Markdown 出力に保持されることを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact]
    public async Task ExportAsync_PreservesMergedCellsFormulaAndImageAsExternalFile()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("申請書", CellStyle.Default, ColumnSpan: 2),
            [new(3, 1)] = new("氏名", CellStyle.Default),
            [new(3, 2)] = new("山田 太郎", CellStyle.Default, Formula: "=A1"),
        };
        var image = new ReportImage(
            new(3, 2),
            0,
            0,
            180,
            64,
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var sheet = new ReportSheet(
            "申請/書",
            cells,
            new Dictionary<int,
            ColumnDefinition> { [1] = new(50), [2] = new(80) },
            new Dictionary<int,
            RowDefinition> { [1] = new(20), [2] = new(20), [3] = new(20) },
            new[] { new CellRange(new(1, 1), new(1, 2)) },
            new PageSettings(),
            Images: new[] { image });
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await new MarkdownExporter().ExportAsync(
                new ReportDocument(new[] { sheet }),
                directory,
                documentName: "sample.xlsx");

            var markdown = await File.ReadAllTextAsync(Path.Combine(directory, "sample.md"));
            Assert.Contains("申請書", markdown);
            Assert.Contains("colspan=\"2\"", markdown);
            Assert.Contains("山田 太郎 (=A1)", markdown);
            Assert.Contains("images/申請_書_img_001.png", Uri.UnescapeDataString(markdown));
            Assert.True(File.Exists(Path.Combine(directory, "images", "申請_書_img_001.png")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>
    /// レイアウト領域が左列から右列の順に読み順へ整列されることを検証します。
    /// </summary>
    [Fact]
    public void LayoutSegmenter_OrdersLeftColumnBeforeRightColumn()
    {
        var style = CellStyle.Default;
        var cells = new[]
        {
            new VisualCell(new(new(1, 1), new(1, 1)), "left", 0, 0, 40, 10, style),
            new VisualCell(new(new(1, 3), new(1, 3)), "right", 100, 0, 40, 10, style),
        };

        var layout = new LayoutSegmenter().Segment(cells);

        Assert.Equal(LayoutSplitDirection.Vertical, layout.SplitDirection);
        Assert.Equal("left", layout.Children[0].Cells.Single().Text);
        Assert.Equal("right", layout.Children[1].Cells.Single().Text);
    }

    /// <summary>
    /// 離れた行の表示位置に途中の行高を累積したオフセットが使われることを検証します。
    /// </summary>
    [Fact]
    public void VisualCellBuilder_UsesCumulativeOffsetsForDistantRows()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("first", CellStyle.Default),
            [new(100_000, 1)] = new("last", CellStyle.Default),
        };
        var rows = Enumerable.Range(1, 100_000)
            .ToDictionary(row => row, _ => new RowDefinition(2));
        var sheet = new ReportSheet(
            "Sheet",
            cells,
            new Dictionary<int,
            ColumnDefinition> { [1] = new(10) },
            rows,
            Array.Empty<CellRange>(),
            new PageSettings());

        var visualCells = new VisualCellBuilder().Build(sheet);

        Assert.Equal(199_998, visualCells[1].Y);
        Assert.Equal(2, visualCells[1].Height);
    }

    /// <summary>
    /// 結合セルの前にある空列が Markdown 表の列配置に保持されることを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact]
    public async Task ExportAsync_PreservesBlankColumnsBeforeMergedCells()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("A", CellStyle.Default),
            [new(1, 3)] = new("C-D", CellStyle.Default, ColumnSpan: 2),
        };
        var sheet = new ReportSheet(
            "Sheet",
            cells,
            Enumerable.Range(1, 4).ToDictionary(column => column, _ => new ColumnDefinition(10)),
            new Dictionary<int,
            RowDefinition> { [1] = new(10) },
            new[] { new CellRange(new(1, 3), new(1, 4)) },
            new PageSettings());
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await new MarkdownExporter().ExportAsync(
                new ReportDocument(new[] { sheet }),
                directory,
                documentName: "gaps.xlsx");

            var markdown = await File.ReadAllTextAsync(Path.Combine(directory, "gaps.md"));
            Assert.Contains(
                "<td>A</td>\n  <td></td>\n  <td colspan=\"2\">C-D</td>",
                markdown.Replace("\r\n", "\n"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
