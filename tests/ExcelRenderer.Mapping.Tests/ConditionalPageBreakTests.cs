using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Mapping;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.TestFixtures;
using Xunit;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Mapping.Tests;

public sealed class ConditionalPageBreakTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conditional_page_breaks_follow_expansion_with_or_without_print_area(bool preservePrintArea)
    {
        using var result = Map(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as item, index"));
            s.Add(SdkWorkbook.Text("A2", "**@page-break if index%10==0"));
            s.Add(SdkWorkbook.Text("B2", "**@item"));
            s.Add(SdkWorkbook.Text("C2", "**@index"));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
        }, JsonSerializer.Serialize(new { items = Enumerable.Range(1, 25) }), preservePrintArea);
        var sheet = result;
        Assert.Equal(new[] { 10, 20 }, Breaks(sheet).OrderBy(x => x));
        Assert.Empty(Breaks(sheet, true));
        Assert.Equal(100, int.Parse(Info(sheet).PageSetup!.Attributes["scale"]));
        for (var index = 0; index < 25; index++)
        {
            Assert.True(Cell(sheet, index + 1, 1).Value.Type == CellValueType.Blank);
            Assert.Equal(index + 1, Number(sheet, index + 1, 2));
            Assert.Equal(index, Number(sheet, index + 1, 3));
        }
    }

    [Fact]
    public void False_page_break_conditions_preserve_fit_mode_and_clear_cells()
    {
        using var result = Map(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as item, index"));
            s.Add(SdkWorkbook.Text("C2", "**@page-break if @index >= 10"));
            s.Add(SdkWorkbook.Text("B2", "**@item"));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
        }, """{"items":[1,2]}""");
        var sheet = result;
        Assert.Empty(Breaks(sheet));
        Assert.Empty(Breaks(sheet, true));
        Assert.True(Cell(sheet, "C1").Value.Type == CellValueType.Blank);
        Assert.True(Cell(sheet, "C2").Value.Type == CellValueType.Blank);
        Assert.Equal(1, int.Parse(Info(sheet).PageSetup!.Attributes["fitToWidth"]));
        Assert.Equal(1, int.Parse(Info(sheet).PageSetup!.Attributes["fitToHeight"]));
    }

    [Fact]
    public void Nested_index_aliases_reset_and_outer_indices_remain_in_scope()
    {
        using var result = Map(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array groups[*] as group, outer"));
            s.Add(SdkWorkbook.Text("A2", "**@start-array @group[*] as item, inner"));
            s.Add(SdkWorkbook.Text("A3", "**@page-break if inner % 2 == 1"));
            s.Add(SdkWorkbook.Text("B3", "**@outer"));
            s.Add(SdkWorkbook.Text("C3", "**@inner"));
            s.Add(SdkWorkbook.Text("A4", "**@end-array"));
            s.Add(SdkWorkbook.Text("A5", "**@end-array"));
        }, """{"groups":[[1,2,3],[4,5]]}""");
        var sheet = result;
        Assert.Equal(new[] { 1, 4 }, Breaks(sheet).OrderBy(x => x));
        Assert.Equal(1, Number(sheet, "B4"));
        Assert.Equal(0, Number(sheet, "C4"));
    }

    [Theory]
    [InlineData("index == 1", new[] { 1 })]
    [InlineData("index != 1", new[] { 2 })]
    [InlineData("index < 2", new[] { 1 })]
    [InlineData("index <= 1", new[] { 1 })]
    [InlineData("index > 0", new[] { 1, 2 })]
    [InlineData("index >= 1", new[] { 1, 2 })]
    [InlineData("index == -1", new int[0])]
    [InlineData("index > 0 && index % 2 == 0", new[] { 2 })]
    [InlineData("index == 1 || index == 2", new[] { 1, 2 })]
    [InlineData("index == 1 || index == 2 && index == 0", new[] { 1 })]
    [InlineData("(index == 1 || index == 2) && index == 0", new int[0])]
    [InlineData("index == 0 && index == 1 || index == 2", new[] { 2 })]
    [InlineData("!(index == 0 || index == 2)", new[] { 1 })]
    [InlineData("!(@index != 1)", new[] { 1 })]
    [InlineData("!!(index == 2)", new[] { 2 })]
    [InlineData("!index == 0 && index < 2", new[] { 1 })]
    [InlineData(" (( @index > 0 ) && (index % 2 == 0 || index == 1)) ", new[] { 1, 2 })]
    public void Conditional_comparisons_apply_at_expanded_positions(string condition, int[] expectedBreaks)
    {
        using var result = Map(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as item, index"));
            s.Add(SdkWorkbook.Text("C2", "**@page-break if " + condition));
            s.Add(SdkWorkbook.Text("B2", "**@item"));
            s.Add(SdkWorkbook.Text("D2", "\\**@page-break if index % 10 == 0"));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
        }, """{"items":[1,2,3]}""");
        var sheet = result;
        Assert.Equal(expectedBreaks, Breaks(sheet).OrderBy(x => x));
        Assert.Equal(expectedBreaks.Length > 0 ? new[] { 2 } : Array.Empty<int>(), Breaks(sheet, true));
        Assert.Equal("**@page-break if index % 10 == 0", Text(sheet, "D1"));
    }

    [Theory]
    [InlineData("index == 1 || item == 1")]
    [InlineData("!(index != 1 && item != 1)")]
    public void Logical_operators_short_circuit_data_evaluation(string condition)
    {
        using var result = Map(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as item, index"));
            s.Add(SdkWorkbook.Text("A2", "**@page-break if " + condition));
            s.Add(SdkWorkbook.Text("B2", "**@index"));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
        }, """{"items":[1,"not numeric"]}""");
        Assert.Contains(1, Breaks(result));
    }

    [Fact]
    public void Deeply_nested_conditions_report_a_mapping_error()
    {
        using var input = Template(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as item, index"));
            s.Add(SdkWorkbook.Text("B2", "**@page-break if " + new string('(', 65) + "index == 0" + new string(')', 65)));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
        });
        using var output = new MemoryStream();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[]}"""));
        Assert.Equal("B2", error.CellAddress);
        Assert.Contains("nested", error.Message);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Condition_evaluation_errors_preserve_output_and_report_original_cell()
    {
        using var input = Template(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as item"));
            s.Add(SdkWorkbook.Text("B2", "**@page-break if item == 1"));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
        });
        using var output = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[1,"invalid"]}"""));
        Assert.Equal("B2", error.CellAddress);
        Assert.Contains("numeric", error.Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Theory]
    [InlineData("**@page-break if missing % 10 == 0")]
    [InlineData("**@page-break if index % 0 == 0")]
    [InlineData("**@page-break if index % 10 = 0")]
    [InlineData("**@page-break if index % 10 == 0 garbage")]
    [InlineData("**@page-break if index == 0 || missing == 0")]
    [InlineData("**@page-break if index < 0 && missing == 0")]
    [InlineData("**@page-break if index == 0 || index % 0 == 0")]
    [InlineData("**@page-break if (index == 0")]
    [InlineData("**@page-break if index == 0)")]
    [InlineData("**@page-break if ()")]
    [InlineData("**@page-break if index == 0 &&")]
    [InlineData("**@page-break if || index == 0")]
    [InlineData("**@page-break if index == 0 & index == 1")]
    [InlineData("**@page-break if index == 0 | index == 1")]
    [InlineData("**@page-break if !")]
    [InlineData("**@page-break if (index + 1) % 10 == 0")]
    public void Invalid_conditions_are_validated_in_empty_blocks(string directive)
    {
        using var input = Template(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as item, index"));
            s.Add(SdkWorkbook.Text("B2", directive));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
        });
        using var output = new MemoryStream();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[]}"""));
        Assert.Equal("B2", error.CellAddress);
        Assert.Equal(directive, error.Expression);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("item")]
    [InlineData("outer")]
    public void Index_aliases_cannot_duplicate_or_shadow_aliases(string indexAlias)
    {
        using var input = Template(s =>
        {
            s.Add(SdkWorkbook.Text("A1", "**@start-array items[*] as outer"));
            s.Add(SdkWorkbook.Text("A2", "**@start-array @outer[*] as item, " + indexAlias));
            s.Add(SdkWorkbook.Text("A3", "**@end-array"));
            s.Add(SdkWorkbook.Text("A4", "**@end-array"));
        });
        using var output = new MemoryStream();
        Assert.Contains("alias", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[]}""")).Message);
    }

    private static MemoryStream Template(Action<List<S.Cell>> arrange, bool printArea = false)
    {
        var cells = new List<S.Cell>();
        arrange(cells);
        var data = new S.SheetData();
        foreach (var group in cells.GroupBy(cell => uint.Parse(new string(cell.CellReference!.Value!.Where(char.IsDigit).ToArray()))).OrderBy(group => group.Key))
        {
            var row = new S.Row { RowIndex = group.Key };
            row.Append(group.OrderBy(cell => cell.CellReference!.Value, StringComparer.Ordinal));
            data.Append(row);
        }

        var worksheet = new S.Worksheet(
            new S.SheetProperties(new S.PageSetupProperties { FitToPage = true }),
            data,
            new S.PageSetup { FitToWidth = 1, FitToHeight = 1 });
        var input = SdkWorkbook.Create(worksheet, name: "Template");
        if (printArea)
        {
            using (var document = SpreadsheetDocument.Open(input, true))
            {
                document.WorkbookPart!.Workbook.Append(new S.DefinedNames(
                    new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "Template!$A$1:$C$30" }));
            }

            input.Position = 0;
        }

        return input;
    }

    private static WorkbookProbe Map(Action<List<S.Cell>> arrange, string json, bool printArea = false)
    {
        using var input = Template(arrange, printArea);
        using var output = new MemoryStream();
        ExcelTemplateMapper.MapJson(input, output, json);
        output.Position = 0;
        return WorkbookProbe.Open(output);
    }
}
