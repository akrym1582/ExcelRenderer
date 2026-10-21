using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Excel;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.Rendering;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed partial class ConditionalFormattingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void LimitSearchUsesVisibilityBandsAndDoesNotWarnForHiddenOutput(bool defaultHidden, bool hideLast)
    {
        using var input = LimitWorkbook(defaultHidden, hideLast);
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 2001, 1000) }).Sheets[0];
            if (hideLast)
            {
                Assert.DoesNotContain(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
            }
            else
            {
                Assert.Equal("A2001", Assert.Single(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded").CellRange);
            }

            Assert.All(sheet.Cells.Values, cell => Assert.Null(cell.Style.Background));
            // Work follows row definitions, independent of the 2,001,000-coordinate area.
            Assert.InRange(metrics["conditionalFormatting.limitTargetProbes"], 1, defaultHidden ? 2 : 2001);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LimitDiagnosticReachesConverterAndItsFailurePolicy(bool fail)
    {
        using var input = LimitWorkbook(false, false);
        using var output = new MemoryStream();
        var request = new RenderRequest
        {
            OutputFormat = OutputFormat.Svg,
            ImageLayout = ImageLayoutMode.Continuous,
            Selection = new() { Ranges = [new("data", new(new(1, 1), new(2001, 1000)))], MaxRangeCells = 2100000 },
            DiagnosticOptions = new() { TreatAsErrors = fail ? ["ConditionalFormattingLimitExceeded"] : [] },
        };
        if (fail)
        {
            var exception = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output)));
            Assert.Contains(exception.CollectedDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
            Assert.Equal(0, output.Length);
        }
        else
        {
            var result = await ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded" && diagnostic.CellRange == "A2001");
            Assert.True(output.Length > 100);
        }
    }

    [Fact]
    public async Task LimitDiagnosticDoesNotLeakToAnExcludedRangeOrSheet()
    {
        using var input = LimitWorkbook(false, false);
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (2002, 1, 2002, 1) }).Sheets[0];
        Assert.DoesNotContain(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            var other = workbook.AddNewPart<WorksheetPart>();
            other.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell { CellReference = "A1", CellValue = new("3") }) { RowIndex = 1 }));
            workbook.Workbook.GetFirstChild<S.Sheets>()!.Append(new S.Sheet { Name = "other", SheetId = 2, Id = workbook.GetIdOfPart(other) });
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Svg, Selection = new() { SheetNames = ["other"] } }, new SingleStreamOutputSink(output));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
    }

    [Fact]
    public void TargetSearchDistinguishesAbsenceFromUnknownAndHonorsCancellation()
    {
        using var input = Book(NumericRows(50), "");
        using var reader = OpenXmlWorkbookReader.Open(input);
        var info = reader.ReadWorksheetInfo(1);
        var options = new ExcelReadOptions { IsConditionalFormattingTarget = (_, _, _) => false };
        var search = new ConditionalTargetSearch(info, options, _ => false, 4);
        OpenXml.Reader.Model.Cells.CellRange[] ranges = [new(new(1, 1), new(5, 5))];
        Assert.Equal(TargetSearchState.Indeterminate, search.Find(ranges, default).State);
        Assert.Equal(TargetSearchState.Absent, search.Find([], default).State);
        using var cancellation = new CancellationTokenSource();
        search = new(info, options, _ =>
        {
            cancellation.Cancel();
            return false;
        });
        Assert.ThrowsAny<OperationCanceledException>(() => search.Find(ranges, cancellation.Token));
    }

    [Fact]
    public async Task SelectedPageTargetSearchSkipsTheHiddenPrefix()
    {
        using var input = LimitWorkbook(false, false);
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Svg,
            Selection = new() { Ranges = [new("data", new(new(1, 1), new(2001, 1000)))], MaxRangeCells = 2100000, Pages = [1] },
        }, new SingleStreamOutputSink(output));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded" && diagnostic.CellRange == "A2001");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LimitDiagnosticForMergedOutputSurvivesAHiddenOrOutsideAnchor(int firstRow)
    {
        using var input = LimitWorkbook(false, false);
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var worksheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            worksheet.Elements<S.ConditionalFormatting>().Single().SequenceOfReferences = new() { InnerText = "A1:ALL2002" };
            worksheet.Append(new S.MergeCells(new S.MergeCell { Reference = "A1:ALL2002" }));
            worksheet.GetFirstChild<S.SheetData>()!.Elements<S.Row>().First().Append(new S.Cell { CellReference = "A1", CellValue = new("2") });
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Svg,
            ImageLayout = ImageLayoutMode.Continuous,
            Selection = new() { Ranges = [new("data", new(new(firstRow, 1), new(2002, 1000)))], MaxRangeCells = 2100000 },
        }, new SingleStreamOutputSink(output));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded" && diagnostic.CellRange == "A1");
    }

    private static MemoryStream LimitWorkbook(bool defaultHidden, bool hideLast)
    {
        var rows = (defaultHidden ? "" : string.Concat(Enumerable.Range(1, 2000).Select(row => $"<row r=\"{row}\" hidden=\"1\"/>"))) + $"<row r=\"2001\" hidden=\"{(hideLast ? 1 : 0)}\"><c r=\"A2001\"><v>2</v></c></row>";
        var input = Book(rows, Rule("expression", "<formula>TRUE</formula>", ranges: "A1:ALL2001"));
        if (defaultHidden)
        {
            using var package = SpreadsheetDocument.Open(input, true);
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.PrependChild(new S.SheetFormatProperties { ZeroHeight = true, DefaultRowHeight = 15 });
        }

        input.Position = 0;
        return input;
    }
}
