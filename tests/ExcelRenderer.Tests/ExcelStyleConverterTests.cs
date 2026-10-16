using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.OpenXml;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;
using D = DocumentFormat.OpenXml.Drawing;

namespace ExcelRenderer.Tests;

public sealed class ExcelStyleConverterTests
{
    [Theory]
    [InlineData("thin", BorderLineStyle.Solid, 0.5)]
    [InlineData("medium", BorderLineStyle.Solid, 1)]
    [InlineData("thick", BorderLineStyle.Solid, 2)]
    [InlineData("hair", BorderLineStyle.Hair, 0.25)]
    [InlineData("double", BorderLineStyle.Double, 0.75)]
    [InlineData("dotted", BorderLineStyle.Dotted, 0.5)]
    [InlineData("dashed", BorderLineStyle.Dashed, 0.5)]
    [InlineData("mediumDashed", BorderLineStyle.Dashed, 1)]
    [InlineData("dashDot", BorderLineStyle.DashDot, 0.5)]
    [InlineData("mediumDashDot", BorderLineStyle.DashDot, 1)]
    [InlineData("dashDotDot", BorderLineStyle.DashDotDot, 0.5)]
    [InlineData("mediumDashDotDot", BorderLineStyle.DashDotDot, 1)]
    [InlineData("slantDashDot", BorderLineStyle.SlantDashDot, 0.5)]
    public void Convert_preserves_excel_border_styles(string excelStyle, BorderLineStyle expectedStyle, double expectedWidth)
    {
        var border = new S.Border();
        border.InnerXml = "<top xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" style=\"" + excelStyle + "\"/>";
        var side = Convert(border: border).Border!.Top!;
        Assert.Equal(expectedStyle, side.LineStyle);
        Assert.Equal(expectedWidth, side.Width);
    }

    [Fact]
    public void Convert_resolves_workbook_theme_colors_tints_and_automatic_border_color()
    {
        var style = Convert(new S.Font(new S.Color { Theme = 4 }),
            new S.Fill(new S.PatternFill(new S.ForegroundColor { Theme = 4, Tint = 0.5 }) { PatternType = S.PatternValues.Solid }),
            new S.Border(new S.TopBorder(new S.Color { Indexed = 64 }) { Style = S.BorderStyleValues.Dotted }), accent: "FF0000");
        Assert.Equal(new ReportColor(255, 0, 0), style.Font.Color);
        Assert.Equal(new ReportColor(255, 128, 128), style.Background);
        Assert.Equal(new ReportColor(0, 0, 0), style.Border!.Top!.Color);
        Assert.Equal(BorderLineStyle.Dotted, style.Border.Top.LineStyle);
    }

    [Fact]
    public void Convert_maps_background_border_alignment_and_font_color()
    {
        var style = Convert(new S.Font(new S.Color { Rgb = "FFC86432" }),
            new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = "FF0A141E" }) { PatternType = S.PatternValues.Solid }),
            new S.Border(new S.TopBorder(new S.Color { Rgb = "FF010203" }) { Style = S.BorderStyleValues.Thick }),
            new S.Alignment { Horizontal = S.HorizontalAlignmentValues.Center, Vertical = S.VerticalAlignmentValues.Center });
        Assert.Equal(new ReportColor(10, 20, 30, 255), style.Background);
        Assert.Equal(new ReportColor(200, 100, 50, 255), style.Font.Color);
        Assert.Equal(2, style.Border!.Top!.Width);
        Assert.Equal(new ReportColor(1, 2, 3, 255), style.Border.Top.Color);
        Assert.Null(style.Border.Left);
        Assert.Equal(HorizontalAlignment.Center, style.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, style.VerticalAlignment);
    }

    [Fact]
    public void Convert_returns_no_background_or_border_for_default_style()
    {
        var style = Convert();
        Assert.Null(style.Background);
        Assert.Null(style.Border);
    }

    [Theory]
    [InlineData(CellValueType.Number, HorizontalAlignment.Right)]
    [InlineData(CellValueType.IsoDate, HorizontalAlignment.Right)]
    [InlineData(CellValueType.Boolean, HorizontalAlignment.Center)]
    [InlineData(CellValueType.String, HorizontalAlignment.Left)]
    [InlineData(CellValueType.Blank, HorizontalAlignment.Left)]
    [InlineData(CellValueType.Error, HorizontalAlignment.Left)]
    public void Convert_resolves_general_alignment_from_saved_value_type(CellValueType type, HorizontalAlignment expected)
    {
        Assert.Equal(expected, Convert(type: type).HorizontalAlignment);
    }

    [Fact]
    public void Convert_reads_shrink_to_fit()
    {
        Assert.True(Convert(alignment: new S.Alignment { ShrinkToFit = true }).ShrinkToFit);
    }

    [Fact]
    public void Convert_resolves_theme_colors_and_tints()
    {
        var style = Convert(new S.Font(new S.Color { Theme = 4 }),
            new S.Fill(new S.PatternFill(new S.ForegroundColor { Theme = 4, Tint = 0.5 }) { PatternType = S.PatternValues.Solid }),
            new S.Border(new S.TopBorder(new S.Color { Theme = 4, Tint = -0.5 }) { Style = S.BorderStyleValues.Thin }), accent: "6496C8");
        Assert.Equal(new ReportColor(100, 150, 200, 255), style.Font.Color);
        Assert.Equal(new ReportColor(178, 202, 227, 255), style.Background);
        Assert.Equal(new ReportColor(39, 75, 111, 255), style.Border!.Top!.Color);
    }

    [Theory]
    [InlineData("80FF0000", 255, 0, 0, 128)]
    [InlineData("FF010203", 1, 2, 3, 255)]
    public void Convert_preserves_rgb_alpha(string rgb, byte red, byte green, byte blue, byte alpha)
    {
        Assert.Equal(new ReportColor(red, green, blue, alpha), Convert(new S.Font(new S.Color { Rgb = rgb })).Font.Color);
    }

    [Fact]
    public void Convert_validates_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => ExcelStyleConverter.Convert(null!, CellValueType.Number));
        Assert.Throws<ArgumentOutOfRangeException>(() => Convert(type: (CellValueType)999));
    }

    private static CellStyle Convert(S.Font? font = null, S.Fill? fill = null, S.Border? border = null, S.Alignment? alignment = null, CellValueType type = CellValueType.String, string? accent = null)
    {
        var styles = SdkWorkbook.Styles(new S.CellFormat { FontId = 0, FillId = 0, BorderId = 0, Alignment = alignment });
        if (font is not null) styles.Fonts!.ReplaceChild(font, styles.Fonts.FirstChild!);
        if (fill is not null) styles.Fills!.ReplaceChild(fill, styles.Fills.FirstChild!);
        if (border is not null) styles.Borders!.ReplaceChild(border, styles.Borders.FirstChild!);
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "text")) { RowIndex = 1 })), styles);
        if (accent is not null)
        {
            using var document = SpreadsheetDocument.Open(input, true);
            document.WorkbookPart!.AddNewPart<ThemePart>().Theme = new D.Theme(new D.ThemeElements(new D.ColorScheme(
                new D.Dark1Color(new D.SystemColor { Val = D.SystemColorValues.WindowText, LastColor = "000000" }),
                new D.Light1Color(new D.SystemColor { Val = D.SystemColorValues.Window, LastColor = "FFFFFF" }),
                new D.Dark2Color(new D.RgbColorModelHex { Val = "000000" }),
                new D.Light2Color(new D.RgbColorModelHex { Val = "FFFFFF" }),
                new D.Accent1Color(new D.RgbColorModelHex { Val = accent })) { Name = "fixture" }));
        }
        input.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(input);
        return ExcelStyleConverter.Convert(reader.ResolveStyle(reader.EnumerateCells(1).Single()), type);
    }
}
