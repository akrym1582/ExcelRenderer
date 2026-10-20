using System.IO.Compression;
using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class SavedPaginationTests
{
    [Theory]
    [InlineData("12", "true", 12)]
    [InlineData("12", "false", 12)]
    [InlineData("12", null, 12)]
    [InlineData("24", "false", 24)]
    [InlineData("24", null, 24)]
    [InlineData(null, null, 18)]
    [InlineData("-1", null, 18)]
    [InlineData("0", null, 0)]
    public async Task Saved_height_is_independent_of_customHeight(string? height, string? custom, double expected)
    {
        using var input = Workbook(height, custom, defaultHeight: "18");
        var sheet = await Read(input);
        Assert.Equal(expected, sheet.Rows[1].Height);
        Assert.Equal(expected, new SheetGeometry(sheet).RowStart(2));
        Assert.Equal(18, sheet.DefaultRowHeight);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task Nonfinite_height_is_rejected_by_the_existing_Reader_policy(string height)
    {
        using var input = Workbook(height, null);
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(input));
    }

    [Fact]
    public async Task Invalid_manual_boolean_is_rejected_like_other_Reader_booleans()
    {
        using var input = Workbook("12", null, breaks: "<rowBreaks><brk id=\"20\" man=\"invalid\"/></rowBreaks>");
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(input));
    }

    [Fact]
    public async Task Missing_default_uses_15_and_hidden_rows_keep_source_coordinates()
    {
        using var input = Workbook("12", null, hiddenRow: 2);
        var sheet = await Read(input);
        Assert.Equal(15, sheet.DefaultRowHeight);
        Assert.True(sheet.Rows[2].IsHidden);
        Assert.Equal(12, new SheetGeometry(sheet).RowStart(3));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("false", 1)]
    [InlineData("0", 1)]
    [InlineData("true", 2)]
    [InlineData("1", 2)]
    public async Task Raw_break_manual_flag_controls_page_plan(string? manual, int expectedPages)
    {
        var flag = manual is null ? string.Empty : $" man=\"{manual}\"";
        using var input = Workbook("12", null, breaks: $"<rowBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"20\"{flag}/></rowBreaks>");
        var sheet = await Read(input);
        var plans = Plan(sheet);
        Assert.Equal(expectedPages, plans.Pages.Count);
        if (expectedPages == 2)
        {
            var second = plans.Build(1, 2, 2, new FixedMeasurer(10));
            Assert.Equal(21, second.Cells.Min(cell => cell.SourceAddress!.Value.Row));
        }
    }

    [Fact]
    public async Task Mixed_breaks_are_filtered_sorted_deduplicated_in_both_axes_and_fit_ignores_them()
    {
        const string breaks = "<rowBreaks><brk id=\"30\" man=\"true\"/><brk id=\"10\"/><brk id=\"20\" man=\"1\"/><brk id=\"20\" man=\"true\"/><brk id=\"0\" man=\"true\"/></rowBreaks><colBreaks><brk id=\"3\" man=\"1\"/><brk id=\"2\" man=\"false\"/><brk id=\"1\" man=\"true\"/><brk id=\"1\" man=\"1\"/></colBreaks>";
        using var input = Workbook("12", null, breaks: breaks);
        var sheet = await Read(input);
        Assert.Equal(new[] { 20, 30 }, sheet.PageSettings.ManualRowBreaks);
        Assert.Equal(new[] { 1, 3 }, sheet.PageSettings.ManualColumnBreaks);
        Assert.Equal(9, Plan(sheet).Pages.Count);
        using var fitInput = Workbook("12", null, breaks: breaks, fit: true);
        var fitSheet = await Read(fitInput);
        Assert.Single(Plan(fitSheet).Pages);
    }

    [Fact]
    public async Task Fifty_saved_rows_fit_720pt_and_text_measurement_never_changes_geometry()
    {
        using var input = Workbook("12", null, defaultHeight: "15");
        var sheet = await Read(input);
        var small = Plan(sheet, 1);
        var huge = Plan(sheet, 1000);
        Assert.Single(small.Pages);
        Assert.Single(huge.Pages);
        Assert.Equal(600, new SheetGeometry(sheet).RowStart(51));
        foreach (var height in new[] { 1, 1000 })
        {
            var page = Plan(sheet, height).Build(0, 1, 1, new FixedMeasurer(height));
            var last = page.Cells.Single(cell => cell.SourceAddress == new CellAddress(50, 1));
            Assert.Equal(36 + (49 * 12), last.Bounds.Y);
            Assert.Equal(12, last.Bounds.Height);
        }

        var before = sheet with { Rows = sheet.Rows.ToDictionary(pair => pair.Key, pair => pair.Value with { Height = 15 }) };
        Assert.Equal(2, Plan(before).Pages.Count);
    }

    private static SheetLayoutPlan Plan(ReportSheet sheet, double height = 10) => new ReportLayoutEngine(new FixedMeasurer(height)).Plan(sheet).Single();

    private static async Task<ReportSheet> Read(Stream input)
    {
        using var prepared = await Core.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        using var font = new Core.Fonts.SingleFontContext(TestSupport.JapaneseFont);
        return new Core.Excel.CoreExcelReader(font).Read(prepared, null).Sheets.Single();
    }

    private static MemoryStream Workbook(string? height, string? custom, string? defaultHeight = null, int? hiddenRow = null, string breaks = "", bool fit = false)
    {
        var stream = SdkWorkbook.Create(new S.Worksheet(new S.SheetData()), SdkWorkbook.Styles());
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());
            var rows = string.Concat(Enumerable.Range(1, 50).Select(row => $"<row r=\"{row}\"{(height is null ? "" : $" ht=\"{height}\"")}{(custom is null ? "" : $" customHeight=\"{custom}\"")}{(row == hiddenRow ? " hidden=\"1\"" : "")}><c r=\"A{row}\" t=\"inlineStr\"><is><t>row {row}</t></is></c><c r=\"D{row}\" t=\"inlineStr\"><is><t>end</t></is></c></row>"));
            writer.Write($"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetPr><pageSetUpPr fitToPage=\"{(fit ? 1 : 0)}\"/></sheetPr><sheetFormatPr{(defaultHeight is null ? "" : $" defaultRowHeight=\"{defaultHeight}\"")}/><sheetData>{rows}</sheetData><pageMargins left=\"0.5\" right=\"0.5\" top=\"0.5\" bottom=\"0.5\" header=\"0\" footer=\"0\"/><pageSetup paperSize=\"1\" scale=\"100\" fitToWidth=\"1\" fitToHeight=\"1\"/>{breaks}</worksheet>");
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class FixedMeasurer(double height) : ITextMeasurer
    {
        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(10, height);
    }
}
