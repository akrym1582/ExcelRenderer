using DocumentFormat.OpenXml.Packaging;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class SheetSelectionTests
{
    [Fact]
    public async Task Multiple_sheets_follow_request_order_and_ignore_duplicates()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            AddSheet(workbook, "Second");
            AddSheet(workbook, "Third");
        });
        using var output = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, output, TestSupport.Options with { SheetNames = ["Third", "First", "Third"] });
        Assert.Equal(2, result.PageCount);
        using var pdf = TestSupport.Open(output.ToArray());
        Assert.Contains("Third", pdf.GetPage(1).Text);
        Assert.Contains("abc 123", pdf.GetPage(2).Text);
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("missing")]
    [InlineData("null-name")]
    public async Task Invalid_selection_does_not_write_output(string scenario)
    {
        var options = scenario switch
        {
            "conflict" => TestSupport.Options with { SheetName = "First", SheetNames = ["First"] },
            "empty" => TestSupport.Options with { SheetNames = [] },
            "invalid" => TestSupport.Options with { SheetNames = [""] },
            "null-name" => TestSupport.Options with { SheetNames = [null!] },
            _ => TestSupport.Options with { SheetNames = ["First", "missing"] },
        };
        using var input = TestSupport.Workbook();
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.ConvertAsync(input, output, options));
        Assert.Equal(0, output.Length);
    }

    private static void AddSheet(WorkbookPart workbook, string name)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(TestWorkbook.Text("A1", name)) { RowIndex = 1 }));
        workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = (uint)workbook.Workbook.Sheets.ChildElements.Count + 1, Name = name });
    }
}
