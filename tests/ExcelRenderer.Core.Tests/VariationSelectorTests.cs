using ExcelRenderer.TestFixtures;
using S = DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class VariationSelectorTests
{
    [Fact]
    public void All_variation_selectors_are_removed_without_changing_base_characters()
    {
        var selectors = Enumerable.Range(0xFE00, 16).Concat(Enumerable.Range(0xE0100, 240));
        foreach (var selector in selectors)
        {
            Assert.Equal("葛A𠮷", DisplayText.WithoutVariationSelectors("葛" + char.ConvertFromUtf32(selector) + "A𠮷"));
        }

        var ordinary = "葛\nA😀e\u0301\uFDFF\uFE10\U000E00FF\U000E01F0";
        Assert.Same(ordinary, DisplayText.WithoutVariationSelectors(ordinary));
        Assert.Equal(string.Empty, DisplayText.WithoutVariationSelectors("\uFE00\U000E0100\U000E01EF"));
    }

    [Fact]
    public async Task Reader_and_layout_use_base_text_for_cells_and_headers()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var sheet = TestSupport.Sheet(workbook);
            sheet.Descendants<S.Text>().Single().Text = "葛\U000E0100飾\uFE00";
            sheet.Append(new S.HeaderFooter(new S.OddHeader("&C辻\U000E01EF")));
        });
        await TestSupport.WithSheet(input, (sheet, font) =>
        {
            Assert.Equal("葛飾", sheet.Cells[new(1, 1)].Text);
            Assert.Equal("辻", sheet.HeaderFooter!.Header.Center);
            var measurer = new PdfSharpTextMeasurer(font);
            var plain = measurer.Layout("葛飾", CellStyle.Default.Font, 20, true);
            var varied = measurer.Layout("葛\U000E0100飾\uFE00", CellStyle.Default.Font, 20, true);
            Assert.Same(plain, varied);
            Assert.All(varied.Lines, line => Assert.Equal(line.Text, DisplayText.WithoutVariationSelectors(line.Text)));
            Assert.Empty(measurer.Layout("\U000E0100", CellStyle.Default.Font, 20, true).Lines);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pdf_text_glyphs_positions_and_sizes_match_base_only_text(bool shrink)
    {
        async Task<byte[]> Render(bool selectors)
        {
            var conversion = await TestSupport.Convert(workbook =>
            {
                var sheet = TestSupport.Sheet(workbook);
                sheet.InsertAt(new S.Columns(new S.Column { Min = 1, Max = 1, Width = 8.710625, CustomWidth = true }), 0);
                sheet.Descendants<S.Text>().Single().Text = selectors ? "葛\U000E0100飾\U000E01EF区\uFE00の帳票" : "葛飾区の帳票";
                workbook.WorkbookStylesPart!.Stylesheet.CellFormats!.GetFirstChild<S.CellFormat>()!.Alignment = new S.Alignment { WrapText = !shrink, ShrinkToFit = shrink };
                sheet.Append(new S.HeaderFooter(new S.OddHeader("&C" + (selectors ? "辻\U000E0100" : "辻")), new S.OddFooter("&C" + (selectors ? "葛\uFE00" : "葛"))));
            });
            return conversion.Pdf;
        }

        var expected = PdfContentProbe.Read(await Render(false)).Texts;
        var actual = PdfContentProbe.Read(await Render(true)).Texts;
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(text => (text.Encoded, text.Origin, text.Size)), actual.Select(text => (text.Encoded, text.Origin, text.Size)));
    }
}
