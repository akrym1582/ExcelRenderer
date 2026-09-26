using ClosedXML.Excel;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// Excel セル書式から描画用セルスタイルへの変換を検証します。
/// </summary>
public sealed class ExcelStyleConverterTests
{
    /// <summary>Excel の各罫線スタイルと太さが描画モデルへ保持されることを検証します。</summary>
    /// <param name="excelStyle">Excel の罫線スタイルです。</param>
    /// <param name="expectedStyle">変換後の線種です。</param>
    /// <param name="expectedWidth">変換後の線幅です。</param>
    [Theory]
    [InlineData(XLBorderStyleValues.Thin, BorderLineStyle.Solid, 0.5)]
    [InlineData(XLBorderStyleValues.Medium, BorderLineStyle.Solid, 1)]
    [InlineData(XLBorderStyleValues.Thick, BorderLineStyle.Solid, 2)]
    [InlineData(XLBorderStyleValues.Hair, BorderLineStyle.Hair, 0.25)]
    [InlineData(XLBorderStyleValues.Double, BorderLineStyle.Double, 0.75)]
    [InlineData(XLBorderStyleValues.Dotted, BorderLineStyle.Dotted, 0.5)]
    [InlineData(XLBorderStyleValues.Dashed, BorderLineStyle.Dashed, 0.5)]
    [InlineData(XLBorderStyleValues.MediumDashed, BorderLineStyle.Dashed, 1)]
    [InlineData(XLBorderStyleValues.DashDot, BorderLineStyle.DashDot, 0.5)]
    [InlineData(XLBorderStyleValues.MediumDashDot, BorderLineStyle.DashDot, 1)]
    [InlineData(XLBorderStyleValues.DashDotDot, BorderLineStyle.DashDotDot, 0.5)]
    [InlineData(XLBorderStyleValues.MediumDashDotDot, BorderLineStyle.DashDotDot, 1)]
    [InlineData(XLBorderStyleValues.SlantDashDot, BorderLineStyle.SlantDashDot, 0.5)]
    public void Convert_preserves_excel_border_styles(XLBorderStyleValues excelStyle, BorderLineStyle expectedStyle, double expectedWidth)
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Borders").Cell(1, 1);
        cell.Style.Border.TopBorder = excelStyle;

        var side = ExcelStyleConverter.Convert(cell).Border!.Top!;

        Assert.Equal(expectedStyle, side.LineStyle);
        Assert.Equal(expectedWidth, side.Width);
    }

    /// <summary>
    /// テーマ色、明暗補正および自動罫線色が具体的な描画色へ解決されることを検証します。
    /// </summary>
    [Fact]
    public void Convert_resolves_workbook_theme_colors_tints_and_automatic_border_color()
    {
        using var workbook = new XLWorkbook();
        workbook.Theme.Accent1 = XLColor.FromArgb(255, 0, 0);
        var cell = workbook.AddWorksheet("Theme").Cell(1, 1);
        cell.Style.Font.FontColor = XLColor.FromTheme(XLThemeColor.Accent1);
        cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
        cell.Style.Border.TopBorder = XLBorderStyleValues.Dotted;
        cell.Style.Border.TopBorderColor = XLColor.FromIndex(64);

        var style = ExcelStyleConverter.Convert(cell);

        Assert.Equal(new ReportColor(255, 0, 0), style.Font.Color);
        Assert.Equal(new ReportColor(255, 128, 128), style.Background);
        Assert.Equal(new ReportColor(0, 0, 0), style.Border!.Top!.Color);
        Assert.Equal(BorderLineStyle.Dotted, style.Border.Top.LineStyle);
    }

    /// <summary>
    /// セルの背景、罫線、配置およびフォント色が描画用スタイルへ変換されることを検証します。
    /// </summary>
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

    /// <summary>
    /// 既定スタイルのセルでは背景色と罫線が設定されないことを検証します。
    /// </summary>
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

    /// <summary>
    /// 標準配置の数値セルが右揃えとして解決されることを検証します。
    /// </summary>
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

    /// <summary>
    /// 標準配置の真偽値セルが中央揃えとして解決されることを検証します。
    /// </summary>
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

    /// <summary>
    /// 標準配置の文字列セルが左揃えとして解決されることを検証します。
    /// </summary>
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

    /// <summary>
    /// セルの縮小表示設定が描画用スタイルへ反映されることを検証します。
    /// </summary>
    [Fact]
    public void Convert_reads_shrink_to_fit()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell(1, 1);
        cell.Style.Alignment.ShrinkToFit = true;

        var style = ExcelStyleConverter.Convert(cell);

        Assert.True(style.ShrinkToFit);
    }

    /// <summary>
    /// テーマ色と明暗補正がセルの実際の色へ解決されることを検証します。
    /// </summary>
    [Fact]
    public void Convert_resolves_theme_colors_and_tints()
    {
        using var workbook = new XLWorkbook();
        workbook.Theme.Accent1 = XLColor.FromArgb(255, 100, 150, 200);
        var cell = workbook.AddWorksheet("Sheet1").Cell(1, 1);
        cell.Style.Font.FontColor = XLColor.FromTheme(XLThemeColor.Accent1);
        cell.Style.Fill.PatternType = XLFillPatternValues.Solid;
        cell.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.5);
        cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.TopBorderColor = XLColor.FromTheme(XLThemeColor.Accent1, -0.5);

        var style = ExcelStyleConverter.Convert(cell);

        Assert.Equal(new ReportColor(100, 150, 200, 255), style.Font.Color);
        Assert.Equal(new ReportColor(178, 202, 227, 255), style.Background);
        Assert.Equal(new ReportColor(39, 75, 111, 255), style.Border!.Top!.Color);
    }
}
