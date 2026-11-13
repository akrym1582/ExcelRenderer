using System.IO.Compression;
using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class SavedPaginationTests
{
    // Core tests own the boundary cases; these cases verify the main reader/model adapter.
    [Fact]
    public void Saved_height_without_customHeight_survives_main_reader_and_adapter()
    {
        using var input = Workbook("12", "false", defaultHeight: "18");
        var sheet = Read(input);
        Assert.Equal(12, sheet.Rows[1].Height);
        Assert.Equal(12, new SheetGeometry(sheet).RowStart(2));
        Assert.Equal(18, sheet.DefaultRowHeight);
    }

    [Fact]
    public void Missing_default_uses_15_and_hidden_rows_keep_source_coordinates()
    {
        using var input = Workbook("12", null, hiddenRow: 2);
        var sheet = Read(input);
        Assert.Equal(15, sheet.DefaultRowHeight);
        Assert.True(sheet.Rows[2].IsHidden);
        Assert.Equal(12, new SheetGeometry(sheet).RowStart(3));
    }

    [Fact]
    public void Mixed_breaks_are_filtered_sorted_deduplicated_in_both_axes_and_fit_ignores_them()
    {
        const string breaks = "<rowBreaks><brk id=\"30\" man=\"true\"/><brk id=\"10\"/><brk id=\"20\" man=\"1\"/><brk id=\"20\" man=\"true\"/><brk id=\"0\" man=\"true\"/></rowBreaks><colBreaks><brk id=\"3\" man=\"1\"/><brk id=\"2\" man=\"false\"/><brk id=\"1\" man=\"true\"/><brk id=\"1\" man=\"1\"/></colBreaks>";
        using var input = Workbook("12", null, breaks: breaks);
        var sheet = Read(input);
        Assert.Equal(new[] { 20, 30 }, sheet.PageSettings.ManualRowBreaks);
        Assert.Equal(new[] { 1, 3 }, sheet.PageSettings.ManualColumnBreaks);
        Assert.Equal(9, Plan(sheet).Pages.Count);
        using var fitInput = Workbook("12", null, breaks: breaks, fit: true);
        var fitSheet = Read(fitInput);
        Assert.Single(Plan(fitSheet).Pages);
    }

    private static SheetLayoutPlan Plan(ReportSheet sheet) => new ReportLayoutEngine(new FixedMeasurer()).Plan(sheet).Single();

    private static ReportSheet Read(Stream input) =>
        CoreIntegration.CoreModelAdapter.ToCore(new Excel.ExcelReader().Read(input).Sheets.Single());

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

    private sealed class FixedMeasurer : ITextMeasurer
    {
        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(10, 10);
    }
}
