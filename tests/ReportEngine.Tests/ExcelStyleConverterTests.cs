using ClosedXML.Excel;
using ReportEngine.Excel;
using ReportEngine.Model;
using Xunit;

namespace ReportEngine.Tests;

public sealed class ExcelStyleConverterTests
{
    [Fact]
    public void Convert_maps_background_border_alignment_and_font_color()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        var cell = worksheet.Cell(1, 1);
        cell.Value = "text";
        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, 10, 20, 30);
        cell.Style.Font.FontColor = XLColor.FromArgb(255, 200, 100, 50);
        cell.Style.Border.TopBorder = XLBorderStyleValues.Thick;
        cell.Style.Border.TopBorderColor = XLColor.FromArgb(255, 1, 2, 3);
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var style = ExcelStyleConverter.Convert(cell);

        Assert.Equal(new ReportColor(10, 20, 30, 255), style.Background);
        Assert.Equal(new ReportColor(200, 100, 50, 255), style.Font.Color);
        Assert.NotNull(style.Border);
        Assert.NotNull(style.Border!.Top);
        Assert.Equal(2, style.Border.Top!.Width);
        Assert.Equal(new ReportColor(1, 2, 3, 255), style.Border.Top.Color);
        Assert.Null(style.Border.Left);
        Assert.Equal(HorizontalAlignment.Center, style.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, style.VerticalAlignment);
    }

    [Fact]
    public void Convert_returns_no_background_or_border_for_default_style()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        var cell = worksheet.Cell(1, 1);
        cell.Value = "text";

        var style = ExcelStyleConverter.Convert(cell);

        Assert.Null(style.Background);
        Assert.Null(style.Border);
    }

    [Fact]
    public void Convert_resolves_general_alignment_right_for_numbers()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        var cell = worksheet.Cell(1, 1);
        cell.Value = 123.45;

        var style = ExcelStyleConverter.Convert(cell);

        Assert.Equal(HorizontalAlignment.Right, style.HorizontalAlignment);
    }

    [Fact]
    public void Convert_resolves_general_alignment_center_for_booleans()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        var cell = worksheet.Cell(1, 1);
        cell.Value = true;

        var style = ExcelStyleConverter.Convert(cell);

        Assert.Equal(HorizontalAlignment.Center, style.HorizontalAlignment);
    }

    [Fact]
    public void Convert_resolves_general_alignment_left_for_text()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        var cell = worksheet.Cell(1, 1);
        cell.Value = "hello";

        var style = ExcelStyleConverter.Convert(cell);

        Assert.Equal(HorizontalAlignment.Left, style.HorizontalAlignment);
    }
}
