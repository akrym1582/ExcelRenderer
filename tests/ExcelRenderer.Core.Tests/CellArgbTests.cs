using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Model;
using ExcelRenderer.OpenXml.Reader;
using Xunit;
using UglyToad.PdfPig.Tokens;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class CellArgbTests
{
    [Fact]
    public void Shared_converter_ignores_alpha_while_source_snapshots_preserve_it()
    {
        using var input = Workbook();
        using var reader = OpenXmlWorkbookReader.Open(input);
        var cells = reader.EnumerateCells(1).ToArray();
        var prefixes = new[] { "00", "01", "80", "FF", "00" };
        for (var row = 2; row <= 6; row++)
        {
            var source = reader.ResolveStyle(cells.Single(cell => cell.Address.Row == row && cell.Address.Column == 2));
            Assert.Equal(prefixes[row - 2] + "000000", source.FontProperties.Color!.Attributes["rgb"]);
            Assert.Equal(Convert.ToByte(prefixes[row - 2], 16).ToString(), source.FontProperties.Color.Attributes["resolvedAlpha"]);
            Assert.Equal(new ReportColor(0, 0, 0), OpenXmlStyleConverter.Convert(source, OpenXml.Reader.Model.Cells.CellValueType.String).Font.Color);
        }
    }

    [Fact]
    public async Task Core_reader_makes_fonts_fills_and_borders_opaque()
    {
        using var input = Workbook();
        await TestSupport.WithSheet(input, (sheet, _) =>
        {
            for (var row = 2; row <= 6; row++)
            {
                Assert.Equal(new ReportColor(0, 0, 0), sheet.Cells[new(row, 2)].Style.Font.Color);
                Assert.Equal(new ReportColor(51, 102, 153), sheet.Cells[new(row, 3)].Style.Background);
                var border = sheet.Cells[new(row, 4)].Style.Border!;
                Assert.All(new[] { border.Left, border.Top, border.Right, border.Bottom },
                    side => Assert.Equal(new ReportColor(204, 51, 0), side!.Color));
            }
        });
    }

    [Fact]
    public async Task Saved_Core_PDF_contains_visible_text_fill_and_border_paints()
    {
        using var input = Workbook();
        using var output = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "10-cell-argb-core.pdf"), output.ToArray());
        Assert.Equal(1, result.PageCount);
        using var pdf = TestSupport.Open(output.ToArray());
        var resources = TestSupport.Entry<DictionaryToken>(pdf, pdf.GetPage(1).Dictionary, "Resources");
        if (resources.Data.ContainsKey("ExtGState"))
            foreach (var token in TestSupport.Entry<DictionaryToken>(pdf, resources, "ExtGState").Data.Values)
            {
                var state = TestSupport.Token<DictionaryToken>(pdf, token);
                foreach (var channel in new[] { "/ca", "/CA" })
                    if (state.Data.ContainsKey(channel[1..]))
                        Assert.Equal(1, TestSupport.Entry<NumericToken>(pdf, state, channel[1..]).Double);
            }
        var probe = PdfContentProbe.Read(output.ToArray());
        Assert.True(probe.Texts.Count >= 10);
        Assert.True(probe.Paints.Count(paint => paint.Operation == "f") >= 5);
        CellBorderAssertions.StackedCells(probe);
        Assert.All(pdf.GetPage(1).Letters, letter => Assert.Equal((0d, 0d, 0d), letter.Color.ToRGBValues()));
        Assert.Equal(5, pdf.GetPage(1).Paths.Count(path => path.IsFilled && path.FillColor!.ToRGBValues() == (0.2, 0.4, 0.6)));
    }

    private static MemoryStream Workbook()
    {
        var styles = TestWorkbook.Styles();
        var rows = new S.SheetData();
        var prefixes = new[] { "00", "01", "80", "FF", "00" };
        for (var index = 0; index < prefixes.Length; index++)
        {
            var prefix = prefixes[index];
            styles.Fonts!.Append(new S.Font(new S.FontSize { Val = 11 }, new S.Color { Rgb = prefix + "000000" }));
            styles.Fills!.Append(new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = prefix + "336699" }) { PatternType = S.PatternValues.Solid }));
            styles.Borders!.Append(new S.Border(
                new S.LeftBorder(new S.Color { Rgb = prefix + "CC3300" }) { Style = S.BorderStyleValues.Thin },
                new S.RightBorder(new S.Color { Rgb = prefix + "CC3300" }) { Style = S.BorderStyleValues.Thin },
                new S.TopBorder(new S.Color { Rgb = prefix + "CC3300" }) { Style = S.BorderStyleValues.Thin },
                new S.BottomBorder(new S.Color { Rgb = prefix + "CC3300" }) { Style = S.BorderStyleValues.Thin }));
            var fontStyle = (uint)styles.CellFormats!.ChildElements.Count;
            styles.CellFormats.Append(
                new S.CellFormat { FontId = (uint)index + 1, ApplyFont = true },
                new S.CellFormat { FillId = (uint)index + 2, ApplyFill = true },
                new S.CellFormat { BorderId = (uint)index + 1, ApplyBorder = true });
            var row = index + 2;
            rows.Append(new S.Row(TestWorkbook.Text("B" + row, "black", fontStyle),
                TestWorkbook.Text("C" + row, "fill", fontStyle + 1), TestWorkbook.Text("D" + row, "border", fontStyle + 2)) { RowIndex = (uint)row });
        }

        return TestWorkbook.Create(new S.Worksheet(rows), styles);
    }
}
