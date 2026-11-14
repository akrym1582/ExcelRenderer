using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class PivotPageSelectionTests
{
    [Theory]
    [InlineData(FormulaDisplayMode.CachedValue, false)]
    [InlineData(FormulaDisplayMode.CachedValue, true)]
    [InlineData(FormulaDisplayMode.Recalculate, false)]
    [InlineData(FormulaDisplayMode.Recalculate, true)]
    public async Task Only_selected_pages_validate_missing_pivot_values(FormulaDisplayMode mode, bool conditionalFormatting)
    {
        var request = new RenderRequest { ReadOptions = new() { FormulaDisplayMode = mode, ApplyConditionalFormatting = conditionalFormatting } };
        using var saved = Workbook(saved: true);
        using var savedOutput = new MemoryStream();
        var all = await ExcelConverter.RenderAsync(saved, request with { Selection = new() { SheetNames = ["report"] } }, new SingleStreamOutputSink(savedOutput));
        Assert.Equal(2, all.Pages.Count);
        Assert.Equal(2, Assert.Single(all.Diagnostics, diagnostic => diagnostic.Code == "PivotRefreshRequired").SourcePageNumber);

        using var input = Workbook();
        using var output = new MemoryStream();
        var first = await ExcelConverter.RenderAsync(input, request with { Selection = new() { Pages = [1] } }, new SingleStreamOutputSink(output));
        Assert.Equal(1, Assert.Single(first.Pages).SourcePageNumber);
        Assert.True(output.Length > 0);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Pivot", StringComparison.Ordinal));

        foreach (var pages in new int[]?[] { [2], null })
        {
            input.Position = 0;
            var sink = new UnopenedSink();
            var failure = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request with { Selection = new() { Pages = pages } }, sink));
            var diagnostic = Assert.Single(failure.CollectedDiagnostics, diagnostic => diagnostic.Code == "PivotSavedValuesMissing");
            Assert.Equal("report", diagnostic.SheetName);
            Assert.Equal("F15:F15", diagnostic.CellRange);
            Assert.Equal(pages is null ? (int?)null : 2, diagnostic.SourcePageNumber);
            Assert.Contains("Summary", diagnostic.Message);
            Assert.Contains("Refresh and save", diagnostic.Message);
            Assert.Empty(failure.CompletedArtifacts);
            Assert.Equal(0, sink.OpenCount);
        }
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    [InlineData(OutputFormat.PostScript)]
    public async Task All_page_output_cannot_suppress_missing_pivot_failure(OutputFormat format)
    {
        using var input = Workbook();
        var sink = new UnopenedSink();
        var request = new RenderRequest
        {
            OutputFormat = format,
            DiagnosticOptions = new() { SuppressedCodes = ["PivotSavedValuesMissing"], MaxDiagnostics = 1 },
        };
        await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request, sink));
        Assert.Equal(0, sink.OpenCount);
        input.Position = 0;
        await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request with { Selection = new() { Pages = [2] } }, sink));
        Assert.Equal(0, sink.OpenCount);
    }

    [Theory]
    [InlineData("print")]
    [InlineData("range")]
    [InlineData("sheet")]
    public async Task Pivot_outside_output_does_not_block_conversion(string selection)
    {
        using var input = Workbook(printArea: selection == "print" ? "report!$A$1:$D$20" : "report!$A$1:$F$20", addOtherSheet: selection == "sheet");
        var request = new RenderRequest
        {
            Selection = selection switch
            {
                "range" => new() { Ranges = [new("report", new(new(1, 1), new(20, 4)))] },
                "sheet" => new() { SheetNames = ["other"] },
                _ => new(),
            },
        };
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output));
        Assert.True(output.Length > 0);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Pivot", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("F1", "report!$1:$1", "report!$A$11:$F$30")]
    [InlineData("A15", "report!$A:$A", "report!$B$1:$F$20")]
    public async Task Repeated_titles_outside_print_area_are_validated_on_selected_page(string pivot, string titles, string printArea)
    {
        using var input = Workbook(pivot: pivot, titles: titles, printArea: printArea);
        var sink = new UnopenedSink();
        var failure = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, new() { Selection = new() { Pages = [2] } }, sink));
        Assert.Contains(failure.CollectedDiagnostics, diagnostic => diagnostic.Code == "PivotSavedValuesMissing" && diagnostic.SourcePageNumber == 2);
        Assert.Equal(0, sink.OpenCount);
    }

    [Fact]
    public async Task Explicit_range_excludes_repeated_titles_outside_selection()
    {
        using var input = Workbook(pivot: "F1", titles: "report!$1:$1", printArea: "report!$A$11:$F$30");
        using var output = new MemoryStream();
        var request = new RenderRequest { Selection = new() { SheetNames = ["report"], Ranges = [new("report", new(new(11, 1), new(30, 6)))], Pages = [2] } };
        var result = await ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output));
        Assert.True(output.Length > 0);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Pivot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task All_pages_reject_missing_pivot_even_beyond_saved_used_cells()
    {
        using var input = Workbook(pivot: "Z100", printArea: string.Empty);
        var sink = new UnopenedSink();
        var failure = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, new(), sink));
        Assert.Contains(failure.CollectedDiagnostics, diagnostic => diagnostic.Code == "PivotSavedValuesMissing");
        Assert.Equal(0, sink.OpenCount);
    }

    [Fact]
    public void Standalone_reader_still_rejects_missing_pivot_values()
    {
        using var input = Workbook();
        var failure = Assert.Throws<InvalidDataException>(() => new ExcelReader().Read(input));
        Assert.Contains("PivotSavedValuesMissing", failure.Message);
    }

    private static MemoryStream Workbook(bool saved = false, string pivot = "F15", string? titles = null, string printArea = "report!$A$1:$F$20", bool addOtherSheet = false)
    {
        var data = new S.SheetData();
        foreach (var row in new uint[] { 1, 11, 15, 21 })
        {
            data.Append(new S.Row(SdkWorkbook.Text("B" + row, "Page content")) { RowIndex = row });
        }

        var formula = data.Elements<S.Row>().First().GetFirstChild<S.Cell>()!;
        formula.InlineString = null;
        formula.DataType = S.CellValues.Number;
        formula.CellFormula = new S.CellFormula("1+1");
        formula.CellValue = new S.CellValue("2");
        var worksheet = new S.Worksheet(
            data,
            new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.Formula("1=1")) { Type = S.ConditionalFormatValues.Expression, FormatId = 0, Priority = 1 }) { SequenceOfReferences = new() { InnerText = "B1:B30" } },
            new S.PageSetup { Scale = 100 },
            new S.RowBreaks(new S.Break { Id = titles == "report!$1:$1" ? 20U : 10U, Min = 0, Max = 16383, ManualPageBreak = true }));
        var styles = SdkWorkbook.Styles();
        styles.Append(new S.DifferentialFormats(new S.DifferentialFormat(new S.Font(new S.Bold()))));
        var input = SdkWorkbook.Create(worksheet, styles, name: "report");
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            var sheet = workbook.WorksheetParts.Single();
            var cache = workbook.AddNewPart<PivotTableCacheDefinitionPart>();
            cache.PivotCacheDefinition = new S.PivotCacheDefinition { RefreshOnLoad = true, Invalid = true };
            var part = sheet.AddNewPart<PivotTablePart>();
            part.AddPart(cache);
            part.PivotTableDefinition = new S.PivotTableDefinition(new S.Location { Reference = pivot + ":" + pivot }) { Name = "Summary", CacheId = 0, DataCaption = "Values" };
            if (saved)
            {
                sheet.Worksheet.GetFirstChild<S.SheetData>()!.Elements<S.Row>().Single(row => row.RowIndex?.Value == 15).Append(SdkWorkbook.Number("F15", "30"));
            }

            var names = new S.DefinedNames(new S.DefinedName(printArea) { Name = "_xlnm.Print_Area", LocalSheetId = 0 });
            if (titles is not null)
            {
                names.Append(new S.DefinedName(titles) { Name = "_xlnm.Print_Titles", LocalSheetId = 0 });
            }

            if (printArea.Length == 0)
            {
                names.RemoveAllChildren();
            }

            workbook.Workbook.Append(names);
            if (addOtherSheet)
            {
                SdkWorkbook.AddSheet(workbook, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "Other sheet")) { RowIndex = 1 })), "other");
            }
        }

        input.Position = 0;
        return input;
    }

    private sealed class UnopenedSink : IRenderOutputSink
    {
        public int OpenCount { get; private set; }

        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
        {
            OpenCount++;
            throw new InvalidOperationException("Output must not be opened.");
        }

        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
