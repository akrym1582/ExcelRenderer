using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Model;
using ExcelRenderer.OpenXml.Reader;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class CellArgbTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "SampleInputs", "CellArgb", "10-cell-argb.xlsx");

    [Fact]
    public void Shared_converter_ignores_alpha_while_source_snapshots_preserve_it()
    {
        using var input = File.OpenRead(FixturePath);
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
        using var input = File.OpenRead(FixturePath);
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
        using var input = File.OpenRead(FixturePath);
        using var output = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "10-cell-argb-core.pdf"), output.ToArray());
        Assert.Equal(1, result.PageCount);
        using var pdf = TestSupport.Open(output.ToArray());
        var states = pdf.Pages[0].Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/ExtGState");
        if (states is not null)
            foreach (var key in states.Elements.Keys)
                foreach (var channel in new[] { "/ca", "/CA" })
                    if (states.Elements.GetDictionary(key)!.Elements.ContainsKey(channel))
                        Assert.Equal(1, states.Elements.GetDictionary(key)!.Elements.GetReal(channel));
        var probe = PdfContentProbe.Read(output.ToArray());
        Assert.True(probe.Texts.Count >= 10);
        Assert.True(probe.Paints.Count(paint => paint.Operation == "f") >= 5);
        // Five stacked rectangles share four edges; equal vertical intervals join.
        Assert.Equal(8, probe.Paints.Count(paint => paint.Operation == "S"));
    }
}
