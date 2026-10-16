using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.TestFixtures;
using S = DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class ReaderOptimizationTests
{
    [Fact]
    public void SharedStylesKeepGeneralAlignmentAndConvertOnlyEffectiveKeys()
    {
        var rows = new S.SheetData();
        for (var row = 1; row <= 10000; row++)
        {
            var cell = row % 3 == 0 ? SdkWorkbook.Number("A" + row, "42") : row % 3 == 1
                ? SdkWorkbook.Text("A" + row, "text") : new S.Cell(new S.CellValue("0")) { CellReference = "A" + row, DataType = S.CellValues.Boolean };
            rows.Append(new S.Row(cell) { RowIndex = (uint)row });
        }
        using var workbook = SdkWorkbook.Create(new S.Worksheet(rows), SdkWorkbook.Styles());

        var (document, metrics) = Read(workbook);
        Assert.Equal(HorizontalAlignment.Left, document.Sheets[0].Cells[new(1, 1)].Style.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Center, document.Sheets[0].Cells[new(2, 1)].Style.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Right, document.Sheets[0].Cells[new(3, 1)].Style.HorizontalAlignment);
        Assert.Equal(3, metrics["readerStyleConversions"]);
        Assert.Equal(9997, metrics["readerStyleCacheHits"]);
        Assert.Equal(4, metrics["readerStyleCacheEntries"]); // Includes the blank default style used by finite-region materialization.
        Assert.False(metrics.ContainsKey("readerScratchWorkbooks"));
    }

    [Fact]
    public void UniqueStylesHaveBoundedCache()
    {
        var rows = new S.SheetData();
        var styles = SdkWorkbook.Styles();
        var formats = new S.NumberingFormats();
        styles.InsertAt(formats, 0);
        for (var row = 1; row <= 8300; row++)
        {
            formats.Append(new S.NumberingFormat { NumberFormatId = (uint)(200 + row), FormatCode = "0\"-" + row + "\"" });
            styles.CellFormats!.Append(new S.CellFormat { NumberFormatId = (uint)(200 + row) });
            rows.Append(new S.Row(SdkWorkbook.Number("A" + row, row.ToString(), (uint)row)) { RowIndex = (uint)row });
        }
        using var workbook = SdkWorkbook.Create(new S.Worksheet(rows), styles);

        var (document, metrics) = Read(workbook);
        Assert.Equal(8300, document.Sheets[0].Cells.Count);
        Assert.Equal(8192, metrics["readerStyleCacheEntries"]);
        Assert.Equal(8300, metrics["readerStyleConversions"]);
    }

    [Fact]
    public void ResolvedStylesHaveValueEqualityAndConvertingDoesNotMutateSource()
    {
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "one"), SdkWorkbook.Text("B1", "two")) { RowIndex = 1 })), SdkWorkbook.Styles());
        using var reader = OpenXml.OpenXmlWorkbookReader.Open(input);
        var cells = reader.EnumerateCells(1).ToArray();
        var first = reader.ResolveStyle(cells[0]);
        var before = first.FontProperties;
        Assert.Equal(ExcelStyleConverter.Convert(first, cells[0].Value.Type), ExcelStyleConverter.Convert(reader.ResolveStyle(cells[1]), cells[1].Value.Type));
        Assert.Same(before, first.FontProperties);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedHyperlinksRetainFormattingWithoutScratchWorkbooks(bool date1904)
    {
        var styles = SdkWorkbook.Styles(new S.CellFormat { NumberFormatId = 9 }, new S.CellFormat { NumberFormatId = 2 });
        var rows = new S.SheetData();
        for (var row = 1; row <= 10000; row++)
        {
            rows.Append(new S.Row(new S.Cell(new S.CellFormula("HYPERLINK(\"https://example.invalid/\",42)"), new S.CellValue("42"))
            {
                CellReference = "A" + row, StyleIndex = row % 2 == 0 ? 1u : 0u,
            }) { RowIndex = (uint)row });
        }
        using var input = SdkWorkbook.Create(new S.Worksheet(rows), styles, date1904);
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = value;
        try
        {
            var document = new ExcelReader().Read(input);
            Assert.Equal("4200%", document.Sheets[0].Cells[new(1, 1)].Text);
            Assert.Equal(42d.ToString("F2"), document.Sheets[0].Cells[new(2, 1)].Text);
            Assert.False(metrics.ContainsKey("readerScratchWorkbooks"));
            Assert.Equal(2, metrics["readerStyleConversions"]);
            Assert.Equal(9998, metrics["readerStyleCacheHits"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    private static (ReportDocument Document, Dictionary<string, double> Metrics) Read(MemoryStream input)
    {
        input.Position = 0;
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = value;
        try
        {
            return (new ExcelReader().Read(input), metrics);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }
}
