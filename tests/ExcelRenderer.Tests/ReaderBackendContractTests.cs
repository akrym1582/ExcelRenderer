using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class ReaderBackendContractTests
{
    [Theory]
    [InlineData(ReaderBackend.OpenXml, "ExplicitOpenXml")]
    [InlineData(ReaderBackend.Auto, "AutoOpenXmlOnly")]
    public void SelectionNotifiesExactlyOnceAfterSuccessfulRead(ReaderBackend backend, string reason)
    {
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Number("A1", "2", 0)) { RowIndex = 1 })), SdkWorkbook.Styles(new S.CellFormat { NumberFormatId = 14 }));
        var selections = new List<ReaderBackendSelection>();
        var sheet = new ExcelReader().Read(input, new() { Backend = backend, BackendSelected = selections.Add }).Sheets.Single();
        Assert.Equal(ReaderBackend.OpenXml, Assert.Single(selections).SelectedBackend);
        Assert.Equal(new[] { reason }, selections[0].ReasonCodes);
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "RegionalNumberFormat");
        Assert.Equal("01-02-00", sheet.Cells[new(1, 1)].Text);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task RemovedAndUnknownBackendsFailBeforeInputAndOutputAndDoNotNotify()
    {
        foreach (var backend in new[] { (ReaderBackend)0, (ReaderBackend)999 })
        {
            var notifications = 0;
            var options = new ExcelReadOptions { Backend = backend, BackendSelected = _ => notifications++ };
            var error = Record.Exception(() => new ExcelReader().Read("does-not-exist.xlsx", options));
            if (backend == (ReaderBackend)0)
            {
                Assert.IsType<NotSupportedException>(error);
                Assert.Contains("OpenXml or Auto", error.Message);
            }
            else Assert.IsType<ArgumentOutOfRangeException>(error);
            foreach (var format in Enum.GetValues<OutputFormat>())
            {
                using var input = new MemoryStream([1, 2, 3]);
                using var output = new MemoryStream();
                output.Write([7, 8, 9]);
                var failure = await Record.ExceptionAsync(() => ExcelConverter.RenderAsync(input, new() { OutputFormat = format, ReadOptions = options }, new SingleStreamOutputSink(output)));
                Assert.Equal(error!.GetType(), failure!.GetType());
                Assert.Equal(0, input.Position);
                Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
            }
            using var coreInput = new MemoryStream([1, 2, 3]);
            using var coreOutput = new MemoryStream();
            var coreFailure = await Record.ExceptionAsync(() => Core.ExcelConverter.ConvertAsync(coreInput, coreOutput, new()
            {
                FontFilePath = "does-not-exist.ttf",
                ReadOptions = new() { Backend = (Core.ReaderBackend)backend, BackendSelected = _ => notifications++ },
            }));
            Assert.Equal(error!.GetType(), coreFailure!.GetType());
            Assert.Equal(0, coreInput.Position);
            Assert.Empty(coreOutput.ToArray());
            Assert.Equal(0, notifications);
        }
        var removed = typeof(ReaderBackend).GetField("ClosedXml")!;
        Assert.Equal(0, (int)(ReaderBackend)removed.GetValue(null)!);
        Assert.NotNull(Attribute.GetCustomAttribute(removed, typeof(ObsoleteAttribute)));
        Assert.Equal(1, (int)ReaderBackend.OpenXml);
        Assert.Equal(2, (int)ReaderBackend.Auto);
    }

    [Theory]
    [InlineData(ReaderBackend.OpenXml)]
    [InlineData(ReaderBackend.Auto)]
    public void BrokenInputDoesNotNotifySuccessfulSelection(ReaderBackend backend)
    {
        var selections = new List<ReaderBackendSelection>();
        using var input = new MemoryStream([1, 2, 3]);
        Assert.ThrowsAny<Exception>(() => new ExcelReader().Read(input, new() { Backend = backend, BackendSelected = selections.Add }));
        Assert.Empty(selections);
    }

    [Theory]
    [InlineData(1, 612, 792)]
    [InlineData(2, 612, 792)]
    [InlineData(5, 612, 1008)]
    [InlineData(8, 841.89, 1190.55)]
    [InlineData(9, 595.276, 841.89)]
    [InlineData(11, 419.53, 595.28)]
    [InlineData(12, 708.66, 1000.63)]
    [InlineData(13, 498.9, 708.66)]
    public void PaperIdentifiersKeepEstablishedDimensionsAndLandscape(int id, double width, double height)
    {
        foreach (var landscape in new[] { false, true })
        {
            using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(), new S.PageSetup { PaperSize = (uint)id, Orientation = landscape ? S.OrientationValues.Landscape : S.OrientationValues.Portrait }));
            var setup = new ExcelReader().Read(input).Sheets[0].PageSettings;
            Assert.Equal(landscape ? height : width, setup.Width);
            Assert.Equal(landscape ? width : height, setup.Height);
        }
    }

    [Fact]
    public void PublicDimensionsIncludeShapeAnchorsOnAnOtherwiseEmptySheet()
    {
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData()));
        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
        {
            var sheet = document.WorkbookPart!.WorksheetParts.Single();
            var drawing = sheet.AddNewPart<DocumentFormat.OpenXml.Packaging.DrawingsPart>();
            drawing.WorksheetDrawing = new DocumentFormat.OpenXml.Drawing.Spreadsheet.WorksheetDrawing();
            sheet.Worksheet.Append(new S.Drawing { Id = sheet.GetIdOfPart(drawing) });
            drawing.WorksheetDrawing.InnerXml = """
                <xdr:oneCellAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <xdr:from><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                  <xdr:ext cx="914400" cy="914400"/>
                  <xdr:sp><xdr:nvSpPr><xdr:cNvPr id="1" name="box"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:sp><xdr:clientData/>
                </xdr:oneCellAnchor>
                """;
        }
        input.Position = 0;
        var result = new ExcelReader().Read(input).Sheets[0];
        Assert.Single(result.Shapes!);
        Assert.Empty(result.Cells);
        Assert.True(result.Columns.ContainsKey(6));
        Assert.True(result.Rows.ContainsKey(5));
    }

    [Fact]
    public void ReaderUsesAnExplicitSnapshotOfCurrentDisplayCulture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Number("A1", "12.5")) { RowIndex = 1 })), SdkWorkbook.Styles(new S.CellFormat { NumberFormatId = 2 }));
            foreach (var item in new[] { ("fr-FR", "12,50"), ("en-US", "12.50") })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(item.Item1);
                input.Position = 0;
                Assert.Equal(item.Item2, new ExcelReader().Read(input).Sheets[0].Cells[new(1, 1)].Text);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void UnrecognizedPaperAndExplicitLengthsHaveDocumentedFallbackDiagnostics()
    {
        var setup = new S.PageSetup { PaperSize = 999 };
        setup.SetAttribute(new DocumentFormat.OpenXml.OpenXmlAttribute("paperWidth", string.Empty, "210mm"));
        setup.SetAttribute(new DocumentFormat.OpenXml.OpenXmlAttribute("paperHeight", string.Empty, "297mm"));
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(), setup));
        var sheet = new ExcelReader().Read(input).Sheets[0];
        Assert.Equal(595.276, sheet.PageSettings.Width);
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "UnsupportedPaperSize");
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "UnsupportedPaperDimensions");
    }
}
