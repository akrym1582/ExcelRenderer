using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using ExcelRenderer.Mapping;
using Xunit;

namespace ExcelRenderer.Mapping.Tests;

public sealed class MappingTests
{
    [Theory]
    [InlineData("**$.name.value")]
    [InlineData("**name.value")]
    [InlineData("**.name.value")]
    [InlineData("**$['name'][\"value\"]")]
    public void Root_paths_and_optional_prefixes(string expression)
    {
        using var result = Map(s => s.Cell("A1").Value = expression, """{"name":{"value":"Alice"}}""");
        Assert.Equal("Alice", result.Worksheet(1).Cell("A1").GetString());
    }

    [Fact]
    public void Typed_values_formatting_dates_nulls_and_escaped_markers()
    {
        using var result = Map(s =>
        {
            s.Cell("A1").Value = "**amount";
            s.Cell("A1").Style.NumberFormat.Format = "0.000";
            s.Cell("A2").Value = "**amount | format(\"N2\", \"en-US\")";
            s.Cell("A3").Value = "**date";
            s.Cell("A4").Value = "**date | date(\"yyyy/MM/dd\")";
            s.Cell("A5").Value = "**enabled";
            s.Cell("A6").Value = "**nothing";
            s.Cell("A7").Value = "\\**literal";
            s.Cell("A8").Value = "prefix **amount";
            s.Cell("A9").Value = "**$['key|with.pipe'] | format(\"000\")";
        }, """{"amount":1234.5,"date":"2026-10-09","enabled":true,"nothing":null,"key|with.pipe":7}""");
        var sheet = result.Worksheet(1);
        Assert.Equal(XLDataType.Number, sheet.Cell("A1").DataType);
        Assert.Equal(1234.5, sheet.Cell("A1").GetDouble());
        Assert.Equal("0.000", sheet.Cell("A1").Style.NumberFormat.Format);
        Assert.Equal("1,234.50", sheet.Cell("A2").GetString());
        Assert.Equal("2026-10-09", sheet.Cell("A3").GetString());
        Assert.Equal("2026/10/09", sheet.Cell("A4").GetString());
        Assert.True(sheet.Cell("A5").GetBoolean());
        Assert.True(sheet.Cell("A6").IsEmpty());
        Assert.Equal("**literal", sheet.Cell("A7").GetString());
        Assert.Equal("prefix **amount", sheet.Cell("A8").GetString());
        Assert.Equal("007", sheet.Cell("A9").GetString());
    }

    [Fact]
    public void Clr_objects_keep_dates_decimals_and_dictionary_keys()
    {
        using var input = Template(s =>
        {
            s.Cell("A1").Value = "**Date";
            s.Cell("A2").Value = "**Amount | format(\"F2\")";
            s.Cell("A3").Value = "**Map['a.b'][0]";
        });
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new
        {
            Date = new DateTime(2026, 10, 9),
            Amount = 12.345m,
            Map = new Dictionary<string, int[]> { ["a.b"] = [42] },
        });
        output.Position = 0;
        using var result = new XLWorkbook(output);
        Assert.Equal(XLDataType.DateTime, result.Worksheet(1).Cell("A1").DataType);
        Assert.Equal(new DateTime(2026, 10, 9), result.Worksheet(1).Cell("A1").GetDateTime());
        Assert.Equal("12.35", result.Worksheet(1).Cell("A2").GetString());
        Assert.Equal(42, result.Worksheet(1).Cell("A3").GetDouble());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public void Single_row_arrays_preserve_styles_merges_formulas_and_following_rows()
    {
        using var result = Map(s =>
        {
            s.Cell("A1").Value = "Header";
            s.Cell("A2").Value = "**items[*].name";
            s.Range("A2:B2").Merge();
            s.Cell("C2").Value = "**$.items[*].quantity";
            s.Cell("C2").Style.NumberFormat.Format = "0.00";
            s.Cell("C2").Style.Font.Bold = true;
            s.Cell("D2").FormulaA1 = "C2*2";
            s.Row(2).Height = 32;
            s.Cell("A3").Value = "Footer";
            s.Cell("D3").FormulaA1 = "SUM(D2:D2)";
            s.PageSetup.PrintAreas.Add("A1:D3");
            s.PageSetup.AddHorizontalPageBreak(2);
            s.Workbook.DefinedNames.Add("FooterCell", "'Template'!$A$3");
        }, """{"items":[{"name":"A","quantity":2},{"name":"B","quantity":3}]}""");
        var sheet = result.Worksheet(1);
        Assert.Equal("A", sheet.Cell("A2").GetString());
        Assert.Equal("B", sheet.Cell("A3").GetString());
        Assert.Equal("Footer", sheet.Cell("A4").GetString());
        Assert.Equal(32, sheet.Row(3).Height);
        Assert.True(sheet.Cell("C3").Style.Font.Bold);
        Assert.Equal("0.00", sheet.Cell("C3").Style.NumberFormat.Format);
        Assert.Contains(sheet.MergedRanges, r => r.RangeAddress.ToStringRelative() == "A3:B3");
        Assert.Equal("C3*2", sheet.Cell("D3").FormulaA1);
        Assert.Equal(6, sheet.Cell("D3").GetDouble());
        Assert.Equal("A1:D4", Assert.Single(sheet.PageSetup.PrintAreas).RangeAddress.ToStringRelative());
        Assert.Contains("$A$4", result.DefinedNames.Single(n => n.Name == "FooterCell").RefersTo);
        Assert.Contains(2, sheet.PageSetup.RowBreaks);
        Assert.Contains(3, sheet.PageSetup.RowBreaks);
    }

    [Fact]
    public void Nested_blocks_expand_parent_aliases_empty_children_and_multiple_sheets()
    {
        using var result = Map(s =>
        {
            s.Cell("A1").Value = "**@start-array $.orders[*] as order";
            s.Cell("A2").Value = "**@order.number";
            s.Cell("A3").Value = "**@start-array @order.lines[*] as line";
            s.Cell("A4").Value = "**@line.name";
            s.Cell("B4").Value = "**@order.number";
            s.Cell("C4").Value = "**title";
            s.Cell("A5").Value = "**@end-array";
            s.Cell("A6").Value = "**@order.total";
            s.Cell("A7").Value = "**@end-array";
            s.Cell("A8").Value = "Footer";
            s.Workbook.AddWorksheet("Other").Cell("A1").Value = "**title";
        }, """{"title":"Root","orders":[{"number":"O1","total":10,"lines":[{"name":"A"},{"name":"B"}]},{"number":"O2","total":0,"lines":[]}]}""");
        var s = result.Worksheet(1);
        Assert.Equal(new[] { "O1", "A", "B", "10", "O2", "0", "Footer" }, Enumerable.Range(1, 7).Select(r => s.Cell(r, 1).GetFormattedString()).ToArray());
        Assert.Equal("O1", s.Cell("B3").GetString());
        Assert.Equal("Root", s.Cell("C3").GetString());
        Assert.Equal("Root", result.Worksheet("Other").Cell("A1").GetString());
        Assert.DoesNotContain(s.CellsUsed(), c => c.DataType == XLDataType.Text && c.GetString().StartsWith("**"));
    }

    [Fact]
    public void Empty_arrays_delete_single_rows_and_blocks()
    {
        using var result = Map(s =>
        {
            s.Cell("A1").Value = "Header";
            s.Cell("A2").Value = "**items[*].name";
            s.Cell("A3").Value = "**@start-array items[*] as item";
            s.Cell("A4").Value = "**@item.name";
            s.Cell("A5").Value = "**@end-array";
            s.Cell("A6").Value = "Footer";
        }, """{"items":[]}""");
        Assert.Equal("Header", result.Worksheet(1).Cell("A1").GetString());
        Assert.Equal("Footer", result.Worksheet(1).Cell("A2").GetString());
        Assert.Equal(2, result.Worksheet(1).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Page_breaks_follow_expansion_and_override_fit_mode()
    {
        using var result = Map(s =>
        {
            s.Cell("A1").Value = "**items[*]";
            s.Cell("C2").Value = "**@page-break";
            s.Cell("A3").Value = "After";
            s.PageSetup.FitToPages(1, 1);
        }, """{"items":[1,2,3]}""");
        var s = result.Worksheet(1);
        Assert.Contains(3, s.PageSetup.RowBreaks);
        Assert.Contains(2, s.PageSetup.ColumnBreaks);
        Assert.True(s.Cell("C4").IsEmpty());
        Assert.Equal(100, s.PageSetup.Scale);
        Assert.Equal(0, s.PageSetup.PagesWide);
        Assert.Equal(0, s.PageSetup.PagesTall);
    }

    [Theory]
    [InlineData("**missing", "was not found")]
    [InlineData("**Name", "was not found")]
    [InlineData("**name..value", "Expected")]
    [InlineData("**name[?(@.x)]", "index")]
    [InlineData("**items[3]", "index")]
    [InlineData("**name | format(\"N2\")", "IFormattable")]
    [InlineData("**name | date(\"yyyy\")", "ISO 8601")]
    [InlineData("**name | unknown(\"N2\")", "Expected")]
    [InlineData("**name | format(1)", "converted")]
    [InlineData("**@unknown.name", "not in scope")]
    public void Invalid_mappings_report_original_cell_and_preserve_output(string expression, string message)
    {
        using var input = Template(s => s.Cell("B7").Value = expression);
        using var output = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        var exception = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"name":"Alice","items":[]}"""));
        Assert.Equal("Template", exception.SheetName);
        Assert.Equal("B7", exception.CellAddress);
        Assert.Equal(expression, exception.Expression);
        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Theory]
    [InlineData("**@start-array items[*] as item", "**@item.name", "", "matching end-array")]
    [InlineData("**@end-array", "", "", "matching start-array")]
    [InlineData("**@start-array items[*] as item", "**@bad.name", "**@end-array", "not in scope")]
    [InlineData("**@start-array items[*] as item", "**items[*].name", "**@end-array", "nested")]
    [InlineData("**@start-array items[*]", "", "**@end-array", "Expected")]
    public void Marker_errors_are_validated_even_for_empty_arrays(string first, string body, string last, string message)
    {
        using var input = Template(s =>
        {
            s.Cell("A1").Value = first;
            s.Cell("A2").Value = body;
            s.Cell("A3").Value = last;
        });
        using var output = new MemoryStream();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[]}"""));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Different_arrays_and_cross_boundary_merges_are_rejected()
    {
        using var input = Template(s =>
        {
            s.Cell("A1").Value = "**first[*]";
            s.Cell("B1").Value = "**second[*]";
        });
        using var output = new MemoryStream();
        Assert.Contains("same array", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, "{}" )).Message);
        using var merged = Template(s =>
        {
            s.Cell("A1").Value = "**items[*]";
            s.Range("A1:A2").Merge();
        });
        Assert.Contains("boundary", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(merged, output, "{}" )).Message);
    }

    [Fact]
    public void Expansion_limit_and_cancellation_leave_destination_untouched()
    {
        using var input = Template(s => s.Cell("A1").Value = "**items[*]");
        using var output = new MemoryStream();
        Assert.Contains("MaxOutputRows", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[1,2,3]}""", new MappingOptions { MaxOutputRows = 2 })).Message);
        Assert.Empty(output.ToArray());
        input.Position = 0;
        Assert.Throws<OperationCanceledException>(() => ExcelTemplateMapper.MapJson(input, output, "{}", cancellationToken: new CancellationToken(true)));
    }


    [Fact]
    public void Multirow_blocks_preserve_vertical_merges_formulas_hidden_rows_and_validation()
    {
        using var result = Map(s =>
        {
            s.Cell("A1").Value = "**@start-array items[*] as item";
            s.Cell("A2").Value = "**@item.value";
            s.Cell("A3").FormulaA1 = "A2*2";
            s.Range("B2:B3").Merge();
            s.Cell("B2").Value = "Merged";
            s.Cell("A2").CreateDataValidation().WholeNumber.Between(1, 20);
            s.Cell("A2").AddConditionalFormat().WhenGreaterThan(5).Fill.SetBackgroundColor(XLColor.Red);
            s.Row(3).Hide();
            s.Cell("A4").Value = "**@end-array";
            s.Cell("A5").Value = "Footer";
        }, """{"items":[{"value":3},{"value":7}]}""");
        var sheet = result.Worksheet(1);
        Assert.Equal(3, sheet.Cell("A1").GetDouble());
        Assert.Equal(7, sheet.Cell("A3").GetDouble());
        Assert.Equal("A1*2", sheet.Cell("A2").FormulaA1);
        Assert.Equal("A3*2", sheet.Cell("A4").FormulaA1);
        Assert.Equal(14, sheet.Cell("A4").GetDouble());
        Assert.Equal("Footer", sheet.Cell("A5").GetString());
        Assert.True(sheet.Row(2).IsHidden);
        Assert.True(sheet.Row(4).IsHidden);
        Assert.Contains(sheet.MergedRanges, r => r.RangeAddress.ToStringRelative() == "B1:B2");
        Assert.Contains(sheet.MergedRanges, r => r.RangeAddress.ToStringRelative() == "B3:B4");
        Assert.Contains(sheet.DataValidations, v => v.Ranges.Any(r => r.Contains(sheet.Cell("A3"))));
        Assert.Contains(sheet.ConditionalFormats, v => v.Ranges.Any(r => r.Contains(sheet.Cell("A3"))));
    }

    [Fact]
    public void Marker_content_alias_shadowing_nonarrays_and_objects_are_rejected()
    {
        using var marker = Template(s =>
        {
            s.Cell("A1").Value = "**@start-array items[*] as item";
            s.Cell("B1").Value = "Would disappear";
            s.Cell("A2").Value = "**@end-array";
        });
        using var output = new MemoryStream();
        Assert.Contains("dedicated", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(marker, output, "{}" )).Message);
        using var shadow = Template(s =>
        {
            s.Cell("A1").Value = "**@start-array items[*] as item";
            s.Cell("A2").Value = "**@start-array @item.lines[*] as item";
            s.Cell("A3").Value = "**@end-array";
            s.Cell("A4").Value = "**@end-array";
        });
        Assert.Contains("shadow", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(shadow, output, "{}" )).Message);
        using var array = Template(s => s.Cell("A1").Value = "**items[*]");
        Assert.Contains("array", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(array, output, """{"items":null}""" )).Message);
        using var value = Template(s => s.Cell("A1").Value = "**items");
        Assert.Contains("scalar", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(value, output, """{"items":{}}""" )).Message);
    }

    [Fact]
    public void Root_scalar_array_indices_and_readonly_dictionaries_are_supported()
    {
        using var json = Map(s => s.Cell("A1").Value = "**items[1]", """{"items":[1,2]}""");
        Assert.Equal(2, json.Worksheet(1).Cell("A1").GetDouble());
        using var scalar = Map(s => s.Cell("A1").Value = "**$", "42");
        Assert.Equal(42, scalar.Worksheet(1).Cell("A1").GetDouble());
        using var input = Template(s => s.Cell("A1").Value = "**value");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["value"] = 7 }));
        output.Position = 0;
        using var mapped = new XLWorkbook(output);
        Assert.Equal(7, mapped.Worksheet(1).Cell("A1").GetDouble());
    }


    [Fact]
    public void Iso_offsets_are_preserved_and_date_conversion_is_explicit()
    {
        using var result = Map(s =>
        {
            s.Cell("A1").Value = "**date | date(\"yyyy-MM-dd HH:mm zzz\")";
            s.Cell("A2").Value = "**date";
        }, """{"date":"2026-10-09T01:02:03+09:00"}""");
        Assert.Equal("2026-10-09 01:02 +09:00", result.Worksheet(1).Cell("A1").GetString());
        Assert.Equal("2026-10-09T01:02:03+09:00", result.Worksheet(1).Cell("A2").GetString());
    }

    [Fact]
    public void Dictionary_paths_remain_case_sensitive_even_with_insensitive_comparer()
    {
        using var input = Template(s => s.Cell("A1").Value = "**Name");
        using var output = new MemoryStream();
        var data = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["name"] = 42 };
        Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, data));
    }

    private static MemoryStream Template(Action<IXLWorksheet> arrange)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Template");
        arrange(sheet);
        var result = new MemoryStream();
        workbook.SaveAs(result);
        result.Position = 0;
        return result;
    }

    private static XLWorkbook Map(Action<IXLWorksheet> arrange, string json)
    {
        using var input = Template(arrange);
        using var output = new MemoryStream();
        ExcelTemplateMapper.MapJson(input, output, json);
        output.Position = 0;
        return new XLWorkbook(output);
    }
}
