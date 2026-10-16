using ExcelRenderer.OpenXml;
using ExcelRenderer.TestFixtures;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;
using static ExcelRenderer.Mapping.Tests.MappingFixture;
using ExcelRenderer.Mapping;
using Xunit;

namespace ExcelRenderer.Mapping.Tests;

public sealed class MappingExpansionTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void Repeated_formulas_are_rejected_before_touching_output(bool block, int count)
    {
        using var input = Fixture(block ? "MappingExpansionTests.Repeated_formulas_are_rejected_before_touching_output.8CAF258AAC33A01F.xlsx" : "MappingExpansionTests.Repeated_formulas_are_rejected_before_touching_output.BA1ABA05D8C21D92.xlsx");
        using var output = new MemoryStream();
        output.Write([7, 8, 9]);
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(0, count).ToArray() }));
        Assert.Equal(block ? "T2" : "T1", error.CellAddress);
        Assert.Equal("MappingFormulaNotSupported", error.ReasonCode);
        Assert.Contains("Calculate values in the caller", error.Message);
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 3)]
    public void Overlapping_names_in_both_scopes_are_rejected(bool local, int count)
    {
        using var input = Fixture(local ? "MappingExpansionTests.Overlapping_names_in_both_scopes_are_rejected.6F66ECE7358D905C.xlsx" : "MappingExpansionTests.Overlapping_names_in_both_scopes_are_rejected.76FC9370F37AAFCD.xlsx");
        using var output = new MemoryStream();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(0, count).ToArray() }));
        Assert.Contains("RepeatedValue", error.Message);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Right_edge_merges_are_copied_without_a_dummy_cell(bool block)
    {
        using var input = Fixture(block ? "MappingExpansionTests.Right_edge_merges_are_copied_without_a_dummy_cell.782E8DF66C6E29BE.xlsx" : "MappingExpansionTests.Right_edge_merges_are_copied_without_a_dummy_cell.7382150770CCC8FE.xlsx");
        using var result = Map(input, new { items = new[] { 10, 20, 30 } });
        var sheet = result;
        Assert.Equal(new[] { "B1:C1", "B2:C2", "B3:C3" }, Info(sheet).MergedRanges.Select(r => r.ToString()).OrderBy(x => x).ToArray());
        for (var row = 1; row <= 3; row++)
        {
            Assert.Equal(row * 10, Number(sheet, row, 1));
            Assert.Equal("Merged", Text(sheet, row, 2));
            Assert.True(Style(sheet, row, 2).FontProperties.Bold == true);
        }
    }

    [Fact]
    public void Direct_regions_preserve_all_nested_rows_styles_breaks_and_following_regions()
    {
        using var input = Fixture("MappingExpansionTests.Direct_regions_preserve_all_nested_rows_styles_breaks_and_following_regions.0CFC84347E6072A5.xlsx");
        var groups = Enumerable.Range(0, 120).Select(id => new { id, items = id % 3 == 0 ? Array.Empty<int>() : Enumerable.Range(id * 10, 10).ToArray() }).ToArray();
        using var result = Map(input, new { groups, tail = new[] { 9001, 9002 } });
        var sheet = result;
        var row = 2;
        foreach (var group in groups)
        {
            Assert.Equal(group.id, Number(sheet, row++, 1));
            foreach (var value in group.items)
            {
                Assert.Equal(value, Number(sheet, row, 1));
                Assert.Equal(group.id, Number(sheet, row, 2));
                Assert.Equal(27, Row(sheet, row).HeightPt);
                Assert.Equal(1, Row(sheet, row).OutlineLevel);
                Assert.True(Row(sheet, row).Hidden == true);
                Assert.Equal("0.00", Style(sheet, row, 1).NumberFormatCode);
                Assert.Equal("FFFF0000", Fill(sheet, row, 26));
                Assert.True(Cell(sheet, row, 3).Value.Type == CellValueType.Blank);
                Assert.Contains(row, Breaks(sheet));
                Assert.Contains(row - 1, Breaks(sheet));
                row++;
            }
        }
        Assert.Equal("Separator", Text(sheet, row++, 1));
        Assert.Equal(9001, Number(sheet, row++, 1));
        Assert.Equal(9002, Number(sheet, row++, 1));
        Assert.Equal("Footer", Text(sheet, row, 1));
        Assert.Equal(row, LastContentRow(sheet));
        Assert.Contains(2, Breaks(sheet, true));
        Assert.Contains(3, Breaks(sheet, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Caller_values_static_names_and_print_settings_remain_supported(int count)
    {
        using var input = Fixture("MappingExpansionTests.Caller_values_static_names_and_print_settings_remain_supported.E8B80A493050EA6C.xlsx");
        using var result = Map(input, new { items = Enumerable.Range(1, count).ToArray(), first = 99, doubled = 198 });
        var sheet = result;
        var footer = count + 2;
        Assert.Equal(99, Number(sheet, "A1"));
        Assert.Equal(99, Number(sheet, footer, 1));
        Assert.Equal(198, Number(sheet, footer, 2));
        Assert.Equal(99, Number(result, "A1", 2));
        Assert.Equal($"Template!$A${footer}", Names(result).Single(n => n.Name == "FooterValue").RawText.Replace("'Template'!", "Template!"));
        Assert.Equal($"Template!$A${footer}", Names(sheet).Single(n => n.Name == "LocalFooter").RawText.Replace("'Template'!", "Template!"));
        Assert.Equal($"A1:B{footer}", PrintArea(sheet));
        Assert.Equal(1, Info(sheet).PrintReferences.Single(item => item.Name == "_xlnm.Print_Titles").Ranges.Single().FirstRow);
    }

    [Fact]
    public void Marker_names_are_rejected_and_names_on_other_sheets_are_allowed()
    {
        using var invalid = Fixture("MappingExpansionTests.Marker_names_are_rejected_and_names_on_other_sheets_are_allowed.DA6DFB580126AEEB.xlsx");
        using var output = new MemoryStream();
        Assert.Contains("MarkerName", Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(invalid, output, new { items = Array.Empty<int>() })).Message);
        Assert.Empty(output.ToArray());

        using var valid = Fixture("MappingExpansionTests.Marker_names_are_rejected_and_names_on_other_sheets_are_allowed.D9157E4BD6C4B236.xlsx");
        using var result = Map(valid, new { items = new[] { 1, 2 } });
        Assert.Equal(42, Number(result, "Z1", 2));
        Assert.Equal("123", Names(result).Single(n => n.Name == "ConstantValue").RawText.Replace("'Template'!", "Template!"));
        Assert.Equal("Other!$Z$1", Names(result).Single(name => name.LocalSheetId == 1).RawText.Replace("'Template'!", "Template!"));
    }

    private static WorkbookProbe Map(Stream input, object data)
    {
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, data);
        output.Position = 0;
        return WorkbookProbe.Open(output);
    }
}
