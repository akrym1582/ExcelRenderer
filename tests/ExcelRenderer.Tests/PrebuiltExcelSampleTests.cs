using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PrebuiltExcelSampleTests
{
    [Fact]
    public void Cell_border_sample_preserves_empty_cells_and_merged_perimeter_fragments()
    {
        SampleOutputTestSupport.ConfigureJapaneseFont();
        var sample = SampleOutputTestSupport.ReadAndLayout("09-cell-border.xlsx");

        Assert.Single(sample.Layout.Pages);
        Assert.Single(sample.Commands.OfType<DrawImageCommand>());
        Assert.NotNull(sample.Sheet.Cells[new(2, 2)].Style.Border!.Top);
        Assert.NotNull(sample.Sheet.Cells[new(33, 38)].Style.Border!.Bottom);

        var heading = sample.Sheet.Cells[new(19, 3)];
        Assert.Equal(25, heading.ColumnSpan);
        Assert.Contains(heading.MergedBorders!, border => border.Address == new CellAddress(19, 27) &&
            border.Border.Right!.Color == new ReportColor(255, 0, 0));

        // P20:AA20 has a bottom border only at AA20. Stretching it across
        // the whole merged cell would hide the dotted top edge of P21:Z21.
        var upper = sample.Sheet.Cells[new(20, 16)];
        var bottom = Assert.Single(upper.MergedBorders!, border => border.Border.Bottom is not null);
        Assert.Equal(new CellAddress(20, 27), bottom.Address);
        var middle = sample.Sheet.Cells[new(21, 16)];
        var dottedTop = middle.MergedBorders!.Where(border => border.Border.Top is not null).ToArray();
        Assert.Equal(11, dottedTop.Length);
        Assert.All(dottedTop, border => Assert.Equal(BorderLineStyle.Dotted, border.Border.Top!.LineStyle));

        var rendered = Assert.Single(sample.Layout.Pages[0].Cells, cell => cell.Cell.Text == heading.Text);
        Assert.All(rendered.MergedBorders!, border =>
        {
            Assert.InRange(border.Bounds.X, rendered.Bounds.X, rendered.Bounds.X + rendered.Bounds.Width);
            Assert.Equal(rendered.Bounds.Height, border.Bounds.Height, 8);
            Assert.Equal(0.3, (border.Border.Top ?? border.Border.Bottom ?? border.Border.Left ?? border.Border.Right)!.Width, 8);
        });
    }

    [Fact]
    public void Excel_reader_preserves_multiple_sheets_for_caller_selection()
    {
        var path = Path.Combine(SampleOutputTestSupport.InputDirectory, "07-multiple-sheets.xlsx");

        var document = new ExcelReader().Read(path);

        Assert.Equal(["売上", "在庫"], document.Sheets.Select(sheet => sheet.Name));
        Assert.Equal("在庫シート", document.Sheets[1].Cells[new(1, 1)].Text);
    }

    [Fact]
    public void Prebuilt_excel_samples_preserve_the_visual_test_features()
    {
        SampleOutputTestSupport.ConfigureJapaneseFont();
        var japanese = SampleOutputTestSupport.ReadAndLayout("01-japanese.xlsx");
        var images = SampleOutputTestSupport.ReadAndLayout("02-image.xlsx");
        var wrappedText = SampleOutputTestSupport.ReadAndLayout("03-wrapped-text.xlsx");
        var textDecoration = SampleOutputTestSupport.ReadAndLayout("04-text-decoration.xlsx");
        var borders = SampleOutputTestSupport.ReadAndLayout("05-borders.xlsx");
        var pagination = SampleOutputTestSupport.ReadAndLayout("06-layout-and-pagination.xlsx");
        var printScaling = SampleOutputTestSupport.ReadAndLayout("08-print-scaling.xlsx");

        Assert.Equal("日本語 PDF 出力サンプル", japanese.Sheet.Cells[new(1, 1)].Text);
        Assert.Equal(2, images.Commands.OfType<DrawImageCommand>().Count());
        Assert.True(wrappedText.Sheet.Cells[new(2, 1)].Style.WrapText);
        Assert.True(textDecoration.Sheet.Cells[new(3, 1)].Style.Font.Bold);
        Assert.NotNull(borders.Sheet.Cells[new(3, 4)].Style.Border);
        Assert.Equal(4, pagination.Layout.Pages.Count);
        Assert.DoesNotContain(
            pagination.Commands.OfType<DrawTextCommand>(),
            command => command.Text.Contains("印刷範囲外", StringComparison.Ordinal));
        Assert.Contains(pagination.Commands.OfType<DrawTextCommand>(), command => command.Text == "ページ 4 / 4");
        Assert.Equal(0.75, printScaling.Sheet.PageSettings.Scale);
        Assert.Single(printScaling.Layout.Pages);
        Assert.Contains(
            printScaling.Commands.OfType<DrawTextCommand>(),
            command => command.Text == "75% 縮小 / ページ 1 / 1");
    }
}
