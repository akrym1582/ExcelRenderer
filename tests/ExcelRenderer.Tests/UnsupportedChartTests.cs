using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class UnsupportedChartTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnchoredChartDiagnosticUsesSelectedOutputBounds(bool excludeChart)
    {
        using var input = CreateChartWorkbook();
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            DiagnosticOptions = new() { DetectUnsupportedFeatures = true },
            Selection = new()
            {
                Ranges = excludeChart ? [new("chart", new(new(1, 4), new(2, 4)))] : null,
            },
        }, new SingleStreamOutputSink(output));
        if (excludeChart)
        {
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "UnsupportedDrawingObject");
        }
        else
        {
            var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "UnsupportedDrawingObject");
            Assert.Equal("A1:B4", diagnostic.CellRange);
            Assert.Equal("chart-1", diagnostic.ObjectId);
            Assert.Equal(1, diagnostic.SourcePageNumber);
            Assert.Equal(1, diagnostic.OccurrenceCount);
        }
    }

    private static MemoryStream CreateChartWorkbook()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("chart");
            sheet.Cell(1, 1).Value = "synthetic";
            sheet.Cell(2, 4).Value = "outside";
            sheet.PageSetup.PrintAreas.Add("A1:D8");
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            var worksheet = document.WorkbookPart!.WorksheetParts.Single();
            var drawing = worksheet.AddNewPart<DrawingsPart>();
            var chart = drawing.AddNewPart<ChartPart>("rIdChart");
            chart.ChartSpace = new C.ChartSpace(new C.Chart());
            drawing.WorksheetDrawing = new Xdr.WorksheetDrawing(new Xdr.TwoCellAnchor(
                new Xdr.FromMarker(new Xdr.ColumnId("0"), new Xdr.ColumnOffset("0"), new Xdr.RowId("0"), new Xdr.RowOffset("0")),
                new Xdr.ToMarker(new Xdr.ColumnId("1"), new Xdr.ColumnOffset("0"), new Xdr.RowId("3"), new Xdr.RowOffset("0")),
                new Xdr.GraphicFrame(
                    new Xdr.NonVisualGraphicFrameProperties(new Xdr.NonVisualDrawingProperties { Id = 5, Name = "chart" }, new Xdr.NonVisualGraphicFrameDrawingProperties()),
                    new Xdr.Transform(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 100000, Cy = 100000 }),
                    new A.Graphic(new A.GraphicData(new C.ChartReference { Id = "rIdChart" }) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" })),
                new Xdr.ClientData()));
            worksheet.Worksheet.Append(new S.Drawing { Id = worksheet.GetIdOfPart(drawing) });
        }

        stream.Position = 0;
        return stream;
    }
}
