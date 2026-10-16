using ExcelRenderer.TestFixtures;
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

    [Theory]
    [InlineData(true, null, 1)]
    [InlineData(false, null, 0)]
    [InlineData(true, "chart", 0)]
    [InlineData(true, "chart-sheet", 1)]
    public async Task ChartsheetHasSheetLevelDiagnosticWithoutInventedCellOrPage(bool detect, string? selected, int expected)
    {
        using var input = CreateChartWorkbook();
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var workbook = document.WorkbookPart!;
            var chartSheet = workbook.AddNewPart<ChartsheetPart>();
            chartSheet.Chartsheet = new S.Chartsheet();
            workbook.Workbook.Sheets!.Append(new S.Sheet { Name = "chart-sheet", SheetId = 2, Id = workbook.GetIdOfPart(chartSheet) });
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            Selection = new() { SheetNames = selected is null ? null : [selected] },
            DiagnosticOptions = new() { DetectUnsupportedFeatures = detect },
        }, new SingleStreamOutputSink(output));
        var diagnostics = result.Diagnostics.Where(d => d.ObjectId == "chart-sheet").ToArray();
        Assert.Equal(expected, diagnostics.Length);
        if (expected == 1)
        {
            Assert.Equal("chart-sheet", diagnostics[0].SheetName);
            Assert.Null(diagnostics[0].CellRange);
            Assert.Null(diagnostics[0].SourcePageNumber);
            Assert.Contains("unavailable", diagnostics[0].Message);
        }

        Assert.Equal(selected == "chart-sheet" ? 0 : 1, result.Pages.Count);
    }

    private static MemoryStream CreateChartWorkbook()
    {
        var stream = new MemoryStream();
        RetainedWorkbook.Copy("UnsupportedChartTests.CreateChartWorkbook.77A571B9C9986FD3.xlsx", stream);

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
