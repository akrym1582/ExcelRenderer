using ClosedXML.Excel;
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
        using var input = Template(s =>
        {
            if (block) s.Cell("A1").Value = "**@start-array items[*] as item";
            var row = block ? 2 : 1;
            s.Cell(row, 1).Value = block ? "**@item" : "**items[*]";
            s.Cell(row, 20).FormulaA1 = "1+1";
            if (block) s.Cell("A3").Value = "**@end-array";
        });
        using var output = new MemoryStream();
        output.Write([7, 8, 9]);
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(0, count).ToArray() }));
        Assert.Equal(block ? "T2" : "T1", error.CellAddress);
        Assert.Equal("=1+1", error.Expression);
        Assert.Contains("Formulas inside repeated", error.Message);
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 3)]
    public void Overlapping_names_in_both_scopes_are_rejected(bool local, int count)
    {
        using var input = Template(s =>
        {
            s.Cell("A1").Value = "**items[*]";
            var names = local ? s.DefinedNames : s.Workbook.DefinedNames;
            names.Add("RepeatedValue", "'Template'!$Z$1");
        });
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
        using var input = Template(s =>
        {
            if (block) s.Cell("A1").Value = "**@start-array items[*] as item";
            var row = block ? 2 : 1;
            s.Cell(row, 1).Value = block ? "**@item" : "**items[*]";
            s.Range(row, 2, row, 3).Merge();
            s.Cell(row, 2).Value = "Merged";
            s.Cell(row, 2).Style.Font.Bold = true;
            if (block) s.Cell("A3").Value = "**@end-array";
        });
        using var result = Map(input, new { items = new[] { 10, 20, 30 } });
        var sheet = result.Worksheet(1);
        Assert.Equal(new[] { "B1:C1", "B2:C2", "B3:C3" }, sheet.MergedRanges.Select(r => r.RangeAddress.ToStringRelative()).OrderBy(x => x).ToArray());
        for (var row = 1; row <= 3; row++)
        {
            Assert.Equal(row * 10, sheet.Cell(row, 1).GetDouble());
            Assert.Equal("Merged", sheet.Cell(row, 2).GetString());
            Assert.True(sheet.Cell(row, 2).Style.Font.Bold);
        }
    }

    [Fact]
    public void Direct_regions_preserve_all_nested_rows_styles_breaks_and_following_regions()
    {
        using var input = Template(s =>
        {
            s.Cell("A1").Value = "Header";
            s.Cell("A2").Value = "**@start-array groups[*] as group";
            s.Cell("A3").Value = "**@group.id";
            s.Cell("A4").Value = "**@start-array @group.items[*] as item";
            s.Cell("A5").Value = "**@item";
            s.Cell("B5").Value = "**@group.id";
            s.Cell("C5").Value = "**@page-break";
            s.Row(5).Height = 27;
            s.Row(5).Hide();
            s.Row(5).OutlineLevel = 1;
            s.Cell("A5").Style.NumberFormat.Format = "0.00";
            s.Cell("Z5").Style.Fill.BackgroundColor = XLColor.Red;
            s.Cell("A6").Value = "**@end-array";
            s.Cell("A7").Value = "**@end-array";
            s.Cell("A8").Value = "Separator";
            s.Cell("A9").Value = "**tail[*]";
            s.Cell("A10").Value = "Footer";
            s.PageSetup.AddHorizontalPageBreak(5);
            s.PageSetup.AddVerticalPageBreak(3);
        });
        var groups = Enumerable.Range(0, 120).Select(id => new { id, items = id % 3 == 0 ? Array.Empty<int>() : Enumerable.Range(id * 10, 10).ToArray() }).ToArray();
        using var result = Map(input, new { groups, tail = new[] { 9001, 9002 } });
        var sheet = result.Worksheet(1);
        var row = 2;
        foreach (var group in groups)
        {
            Assert.Equal(group.id, sheet.Cell(row++, 1).GetDouble());
            foreach (var value in group.items)
            {
                Assert.Equal(value, sheet.Cell(row, 1).GetDouble());
                Assert.Equal(group.id, sheet.Cell(row, 2).GetDouble());
                Assert.Equal(27, sheet.Row(row).Height);
                Assert.Equal(1, sheet.Row(row).OutlineLevel);
                Assert.True(sheet.Row(row).IsHidden);
                Assert.Equal("0.00", sheet.Cell(row, 1).Style.NumberFormat.Format);
                Assert.Equal(XLColor.Red, sheet.Cell(row, 26).Style.Fill.BackgroundColor);
                Assert.True(sheet.Cell(row, 3).IsEmpty());
                Assert.Contains(row, sheet.PageSetup.RowBreaks);
                Assert.Contains(row - 1, sheet.PageSetup.RowBreaks);
                row++;
            }
        }
        Assert.Equal("Separator", sheet.Cell(row++, 1).GetString());
        Assert.Equal(9001, sheet.Cell(row++, 1).GetDouble());
        Assert.Equal(9002, sheet.Cell(row++, 1).GetDouble());
        Assert.Equal("Footer", sheet.Cell(row, 1).GetString());
        Assert.Equal(row, sheet.LastRowUsed()!.RowNumber());
        Assert.Contains(2, sheet.PageSetup.ColumnBreaks);
        Assert.Contains(3, sheet.PageSetup.ColumnBreaks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Outside_formulas_names_and_print_settings_remain_supported(int count)
    {
        using var input = Template(s =>
        {
            s.Cell("A1").FormulaA1 = "A5";
            s.Cell("A2").Value = "**@start-array items[*] as item";
            s.Cell("A3").Value = "**@item";
            s.Cell("A4").Value = "**@end-array";
            s.Cell("A5").Value = 99;
            s.Cell("B5").FormulaA1 = "FooterValue*2";
            s.Workbook.DefinedNames.Add("FooterValue", "'Template'!$A$5");
            s.DefinedNames.Add("LocalFooter", "'Template'!$A$5");
            s.PageSetup.PrintAreas.Add("A1:B5");
            s.PageSetup.SetRowsToRepeatAtTop(1, 1);
            s.Workbook.AddWorksheet("Other").Cell("A1").FormulaA1 = "Template!A5";
        });
        using var result = Map(input, new { items = Enumerable.Range(1, count).ToArray() });
        var sheet = result.Worksheet(1);
        var footer = count + 2;
        Assert.Equal(99, sheet.Cell("A1").GetDouble());
        Assert.Equal(99, sheet.Cell(footer, 1).GetDouble());
        Assert.Equal(198, sheet.Cell(footer, 2).GetDouble());
        Assert.Equal(99, result.Worksheet("Other").Cell("A1").GetDouble());
        Assert.Equal($"Template!$A${footer}", result.DefinedNames.Single(n => n.Name == "FooterValue").RefersTo);
        Assert.Equal($"Template!$A${footer}", sheet.DefinedNames.Single(n => n.Name == "LocalFooter").RefersTo);
        Assert.Equal($"A1:B{footer}", Assert.Single(sheet.PageSetup.PrintAreas).RangeAddress.ToStringRelative());
        Assert.Equal(1, sheet.PageSetup.FirstRowToRepeatAtTop);
    }

    [Fact]
    public void Marker_names_are_rejected_and_names_on_other_sheets_are_allowed()
    {
        using var invalid = Template(s =>
        {
            s.Cell("A1").Value = "**@start-array items[*] as item";
            s.Cell("A2").Value = "**@item";
            s.Cell("A3").Value = "**@end-array";
            s.Workbook.AddWorksheet("Other").DefinedNames.Add("MarkerName", "Template!$A$3");
        });
        using var output = new MemoryStream();
        Assert.Contains("MarkerName", Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(invalid, output, new { items = Array.Empty<int>() })).Message);
        Assert.Empty(output.ToArray());

        using var valid = Template(s =>
        {
            s.Cell("A1").Value = "**items[*]";
            var other = s.Workbook.AddWorksheet("Other");
            other.Cell("Z1").Value = 42;
            other.DefinedNames.Add("OtherValue", "Other!$Z$1");
            s.Workbook.DefinedNames.Add("ConstantValue", "123");
        });
        using var result = Map(valid, new { items = new[] { 1, 2 } });
        Assert.Equal(42, result.Worksheet("Other").Cell("Z1").GetDouble());
        Assert.Equal("123", result.DefinedNames.Single(n => n.Name == "ConstantValue").RefersTo);
        Assert.Equal("Other!$Z$1", result.Worksheet("Other").DefinedNames.Single().RefersTo);
    }

    private static MemoryStream Template(Action<IXLWorksheet> arrange)
    {
        using var workbook = new XLWorkbook();
        arrange(workbook.AddWorksheet("Template"));
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static XLWorkbook Map(Stream input, object data)
    {
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, data);
        output.Position = 0;
        return new XLWorkbook(output);
    }
}
