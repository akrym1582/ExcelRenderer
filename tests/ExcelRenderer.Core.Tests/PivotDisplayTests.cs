using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class PivotDisplayTests
{
    [Fact]
    public async Task Missing_pivot_output_fails_before_writing_pdf()
    {
        using var input = TestSupport.Workbook(workbook => Pivot(workbook, false, true));
        using var pdf = new MemoryStream();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => ExcelConverter.ConvertAsync(input, pdf, TestSupport.Options));
        Assert.Contains("PivotSavedValuesMissing", exception.Message);
        Assert.Contains("Summary", exception.Message);
        Assert.Equal(0, pdf.Length);
    }

    [Fact]
    public async Task Missing_pivot_on_second_page_fails_before_writing_pdf()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            Pivot(workbook, false, true);
            var sheet = workbook.WorksheetParts.Single();
            sheet.PivotTableParts.Single().PivotTableDefinition.Location!.Reference = "F15:F15";
            sheet.Worksheet.Append(new S.RowBreaks(new S.Break { Id = 10, Min = 0, Max = 16383, ManualPageBreak = true }));
            TestSupport.Print(workbook, "First!$A$1:$F$20");
        });
        using var pdf = new MemoryStream();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => ExcelConverter.ConvertAsync(input, pdf, TestSupport.Options));
        Assert.Contains("PivotSavedValuesMissing", exception.Message);
        Assert.Equal(0, pdf.Length);
    }

    [Fact]
    public async Task Missing_pivot_on_unselected_sheet_does_not_block_output()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            Pivot(workbook, false, true);
            var other = workbook.AddNewPart<WorksheetPart>();
            other.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell { CellReference = "A1", CellValue = new S.CellValue("30") }) { RowIndex = 1 }));
            workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(other), SheetId = 2, Name = "Other" });
        });
        using var pdf = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, pdf, TestSupport.Options with { SheetName = "Other" });
        Assert.Equal(1, result.PageCount);
        Assert.True(pdf.Length > 0);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Pivot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_pivot_outside_explicit_print_area_does_not_block_source_data()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            Pivot(workbook, false, true);
            TestSupport.Print(workbook, "First!$A$1:$D$6");
        });
        using var pdf = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, pdf, TestSupport.Options);
        Assert.True(pdf.Length > 0);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "PivotRefreshRequired");
    }

    [Theory]
    [InlineData("First!$A$5:$F$10", "First!$4:$4")]
    [InlineData("First!$G$1:$J$6", "First!$F:$F")]
    public async Task Missing_pivot_in_repeated_titles_fails_before_writing_pdf(string printArea, string titles)
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            Pivot(workbook, false, true);
            TestSupport.Print(workbook, printArea);
            TestSupport.Print(workbook, titles, "_xlnm.Print_Titles");
        });
        using var pdf = new MemoryStream();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => ExcelConverter.ConvertAsync(input, pdf, TestSupport.Options));
        Assert.Contains("PivotSavedValuesMissing", exception.Message);
        Assert.Equal(0, pdf.Length);
    }

    [Fact]
    public async Task Standalone_core_reader_rejects_missing_pivot_without_diagnostic_collector()
    {
        using var input = TestSupport.Workbook(workbook => Pivot(workbook, false, true));
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        var exception = Assert.Throws<InvalidDataException>(() => new CoreExcelReader(font).Read(prepared, null));
        Assert.Contains("PivotSavedValuesMissing", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_pivot_cells_render_and_refresh_state_is_reported(bool stale)
    {
        using var input = TestSupport.Workbook(workbook => Pivot(workbook, true, stale));
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var diagnostics = new DiagnosticCollector();
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        var sheet = new CoreExcelReader(font, diagnostics).Read(prepared, diagnostics).Sheets.Single();
        Assert.Equal("30", sheet.Cells[new CellAddress(4, 6)].Text);
        Assert.Equal(stale, diagnostics.ToArray().Any(d => d.Code == "PivotRefreshRequired"));
    }

    [Fact]
    public async Task Number_format_colors_do_not_leak_between_cells_with_same_style()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var styles = workbook.WorkbookStylesPart!.Stylesheet;
            styles.NumberingFormats = new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "0;[Red](0);[Blue]0" });
            styles.CellFormats!.GetFirstChild<S.CellFormat>()!.NumberFormatId = 164;
            var data = TestSupport.Sheet(workbook).GetFirstChild<S.SheetData>()!;
            data.RemoveAllChildren();
            data.Append(new S.Row(new S.Cell { CellReference = "A1", CellValue = new S.CellValue("-2") }, new S.Cell { CellReference = "B1", CellValue = new S.CellValue("2") }, new S.Cell { CellReference = "C1", CellValue = new S.CellValue("0") }) { RowIndex = 1 });
        });
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        var sheet = new CoreExcelReader(font).Read(prepared, null).Sheets.Single();
        Assert.Equal(new ReportColor(255, 0, 0), sheet.Cells[new CellAddress(1, 1)].Style.Font.Color);
        Assert.NotEqual(new ReportColor(255, 0, 0), sheet.Cells[new CellAddress(1, 2)].Style.Font.Color);
        Assert.Equal(new ReportColor(0, 0, 255), sheet.Cells[new CellAddress(1, 3)].Style.Font.Color);
    }

    private static void Pivot(WorkbookPart workbook, bool saved, bool stale)
    {
        var sheet = workbook.WorksheetParts.Single();
        var cache = workbook.AddNewPart<PivotTableCacheDefinitionPart>();
        cache.PivotCacheDefinition = new S.PivotCacheDefinition { RefreshOnLoad = stale, Invalid = stale };
        var pivot = sheet.AddNewPart<PivotTablePart>();
        pivot.AddPart(cache);
        pivot.PivotTableDefinition = new S.PivotTableDefinition(new S.Location { Reference = "F4:F4" }) { Name = "Summary", CacheId = 0, DataCaption = "Values" };
        if (saved)
        {
            sheet.Worksheet.GetFirstChild<S.SheetData>()!.Append(new S.Row(new S.Cell { CellReference = "F4", CellValue = new S.CellValue("30") }) { RowIndex = 4 });
        }
    }
}
