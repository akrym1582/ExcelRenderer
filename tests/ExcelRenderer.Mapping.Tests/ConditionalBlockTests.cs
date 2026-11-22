using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Mapping;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.TestFixtures;
using Xunit;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Mapping.Tests;

public sealed class ConditionalBlockTests
{
    [Theory]
    [InlineData("""{"show":true,"invalid":"must not be read","items":[{"name":"A","note":true},{"name":"B","note":false}]}""", true)]
    [InlineData("""{"show":false}""", false)]
    [InlineData("""{"show":true,"items":[]}""", false)]
    [InlineData("""{"show":null}""", false)]
    [InlineData("""{}""", false)]
    public void Nested_blocks_compact_rows_and_preserve_alias_scope(string json, bool details)
    {
        using var input = Template(
            "**@if $.show",
            "**@ start-array $.items[*] as item, index",
            "**@   item.name",
            "**@   if @item.note && (index % 2 == 0 || $.invalid)",
            "**@     if !$.hideNote",
            "Note",
            "**@     endif",
            "**@   endif",
            "**@ end-array",
            "**@endif",
            "Total",
            @"\**@  endif");
        using var output = new MemoryStream();
        ExcelTemplateMapper.MapJson(input, output, json);
        using var result = WorkbookProbe.Open(output);

        if (details)
        {
            Assert.Equal("A", Text(result, "A1"));
            Assert.Equal("Note", Text(result, "A2"));
            Assert.Equal("B", Text(result, "A3"));
        }

        Assert.Equal("Total", Text(result, details ? "A4" : "A1"));
        Assert.Equal("**@  endif", Text(result, details ? "A5" : "A2"));
        Assert.Equal(details ? 5 : 2, LastContentRow(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Conditional_rows_move_merges_print_area_and_page_breaks(bool show)
    {
        using var input = Template("Header", "**@if $.show", "Details", "**@  page-break", "**@endif", "Total");
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var workbook = document.WorkbookPart!;
            workbook.WorksheetParts.Single().Worksheet.Append(new S.MergeCells(
                new S.MergeCell { Reference = "A3:B3" },
                new S.MergeCell { Reference = "A6:B6" }));
            workbook.Workbook.Append(new S.DefinedNames(
                new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "Template!$A$1:$B$6" }));
        }

        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { show });
        using var result = WorkbookProbe.Open(output);

        Assert.Equal(show ? "A1:B4" : "A1:B2", PrintArea(result));
        Assert.Equal(show ? new[] { "A2:B2", "A4:B4" } : new[] { "A2:B2" },
            Xml(result).GetFirstChild<S.MergeCells>()!.Elements<S.MergeCell>().Select(cell => cell.Reference!.Value!).OrderBy(reference => reference));
        Assert.Equal(show ? new[] { 2 } : Array.Empty<int>(), Breaks(result));
    }

    [Fact]
    public void False_block_skips_value_and_image_resolution()
    {
        using var input = Template("**@if $.show", "**@   image $.absentImage", "**absentValue", "**@endif", "Total");
        using var output = new MemoryStream();
        ExcelTemplateMapper.MapJson(input, output, "{}");
        using var result = WorkbookProbe.Open(output);

        Assert.Equal("Total", Text(result, "A1"));
    }

    [Theory]
    [InlineData("**@endif", "A3", "matching if")]
    [InlineData("**@if", "A2", "Boolean data path")]
    [InlineData("**@if @unknown", "A2", "not in scope")]
    [InlineData("**@if $.show", "A1", "matching endif")]
    [InlineData("**@start-array $.items[*] as item", "A3", "matching if")]
    public void Invalid_blocks_report_original_cell_even_inside_a_false_block(string marker, string address, string message)
    {
        using var input = Template("**@if $.hidden", marker, "**@endif");
        using var output = new MemoryStream();

        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, "{}"));
        Assert.Equal(address, error.CellAddress);
        Assert.Contains(message, error.Message);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Marker_rows_must_be_dedicated_and_unmerged(bool merged)
    {
        using var input = Template("**@if $.show", "Details", "**@endif");
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var worksheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet;
            if (merged)
            {
                worksheet.Append(new S.MergeCells(new S.MergeCell { Reference = "A1:B1" }));
            }
            else
            {
                worksheet.GetFirstChild<S.SheetData>()!.Elements<S.Row>().First().Append(SdkWorkbook.Text("B1", "Extra"));
            }
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, "{}"));
        Assert.Equal("A1", error.CellAddress);
        Assert.Contains("dedicated", error.Message);
    }

    [Fact]
    public void A_non_boolean_path_reports_the_if_cell()
    {
        using var input = Template("**@  if $.show", "Details", "**@endif");
        using var output = new MemoryStream();

        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"show":"false"}"""));
        Assert.Equal("A1", error.CellAddress);
        Assert.Equal("**@  if $.show", error.Expression);
        Assert.Contains("Boolean", error.Message);
    }

    private static MemoryStream Template(params string[] values)
    {
        var rows = values.Select((value, index) => new S.Row(SdkWorkbook.Text($"A{index + 1}", value)) { RowIndex = (uint)index + 1 });
        return SdkWorkbook.Create(new S.Worksheet(new S.SheetData(rows)), name: "Template");
    }
}
