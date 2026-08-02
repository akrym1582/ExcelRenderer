using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PrebuiltExcelSampleTests
{
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

        Assert.Equal("日本語 PDF 出力サンプル", japanese.Sheet.Cells[new(1, 1)].Text);
        Assert.Equal(2, images.Commands.OfType<DrawImageCommand>().Count());
        Assert.True(wrappedText.Sheet.Cells[new(2, 1)].Style.WrapText);
        Assert.True(textDecoration.Sheet.Cells[new(3, 1)].Style.Font.Bold);
        Assert.NotNull(borders.Sheet.Cells[new(3, 4)].Style.Border);
        Assert.Equal(4, pagination.Layout.Pages.Count);
        Assert.DoesNotContain(pagination.Commands.OfType<DrawTextCommand>(),
            command => command.Text.Contains("印刷範囲外", StringComparison.Ordinal));
        Assert.Contains(pagination.Commands.OfType<DrawTextCommand>(), command => command.Text == "ページ 4 / 4");
    }
}
