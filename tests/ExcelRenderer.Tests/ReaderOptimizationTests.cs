using ClosedXML.Excel;
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
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("mixed");
        for (var row = 1; row <= 10000; row++)
        {
            sheet.Cell(row, 1).Value = (row % 3) switch
            {
                0 => (XLCellValue)42,
                1 => "text",
                _ => false,
            };
        }

        var (document, metrics) = Read(workbook);
        Assert.Equal(HorizontalAlignment.Left, document.Sheets[0].Cells[new(1, 1)].Style.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Center, document.Sheets[0].Cells[new(2, 1)].Style.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Right, document.Sheets[0].Cells[new(3, 1)].Style.HorizontalAlignment);
        Assert.Equal(3, metrics["readerStyleConversions"]);
        Assert.Equal(9997, metrics["readerStyleCacheHits"]);
        Assert.Equal(3, metrics["readerStyleCacheEntries"]);
        Assert.Equal(0, metrics["readerScratchWorkbooks"]);
    }

    [Fact]
    public void UniqueStylesHaveBoundedCache()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("unique");
        for (var row = 1; row <= 8300; row++)
        {
            sheet.Cell(row, 1).Value = row;
            sheet.Cell(row, 1).Style.NumberFormat.Format = "0\"-" + row + "\"";
        }

        var (document, metrics) = Read(workbook);
        Assert.Equal(8300, document.Sheets[0].Cells.Count);
        Assert.Equal(8192, metrics["readerStyleCacheEntries"]);
        Assert.Equal(8300, metrics["readerStyleConversions"]);
    }

    [Fact]
    public void ClosedXmlPublicStyleEqualityIsValueBasedAndScratchAssignmentDoesNotMutateSource()
    {
        using var source = new XLWorkbook();
        using var scratch = new XLWorkbook();
        var sheet = source.AddWorksheet("source");
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Style.Font.Bold = true;
        var first = sheet.Cell(1, 1).Style;
        var second = sheet.Cell(2, 1).Style;
        var hash = first.GetHashCode();
        Assert.Equal(first, second);
        Assert.Equal(hash, second.GetHashCode());
        var cached = scratch.AddWorksheet("scratch").Cell(1, 1);
        cached.Style = first;
        cached.Clear(XLClearOptions.All);
        Assert.True(first.Font.Bold);
        Assert.Equal(hash, first.GetHashCode());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedHyperlinksShareOneScratchAndRetainFormatting(bool date1904)
    {
        using var workbook = new XLWorkbook { Use1904DateSystem = date1904 };
        var sheet = workbook.AddWorksheet("links");
        for (var row = 1; row <= 10000; row++)
        {
            sheet.Cell(row, 1).FormulaA1 = "HYPERLINK(\"https://example.invalid/\",42)";
            sheet.Cell(row, 1).Style.NumberFormat.Format = row % 2 == 0 ? "0.00" : "0%";
        }

        using var input = new MemoryStream();
        workbook.SaveAs(input);
        using (var zip = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            System.Xml.Linq.XDocument xml;
            using (var read = entry.Open())
            {
                xml = System.Xml.Linq.XDocument.Load(read);
            }

            System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var cell in xml.Descendants(ns + "c"))
            {
                cell.Element(ns + "v")?.Remove();
                cell.Add(new System.Xml.Linq.XElement(ns + "v", "42"));
            }

            entry.Delete();
            using var write = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            xml.Save(write);
        }

        input.Position = 0;
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = value;
        try
        {
            var document = new ExcelReader().Read(input);
            Assert.Equal("4200%", document.Sheets[0].Cells[new(1, 1)].Text);
            Assert.Equal(42d.ToString("F2"), document.Sheets[0].Cells[new(2, 1)].Text);
            Assert.Equal(1, metrics["readerScratchWorkbooks"]);
            Assert.Equal(2, metrics["readerStyleConversions"]);
            Assert.Equal(9998, metrics["readerStyleCacheHits"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    private static (ReportDocument Document, Dictionary<string, double> Metrics) Read(XLWorkbook workbook)
    {
        using var input = new MemoryStream();
        workbook.SaveAs(input);
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
