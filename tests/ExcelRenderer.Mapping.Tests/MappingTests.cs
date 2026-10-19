using System.Globalization;
using System.Text.Json;
using ExcelRenderer.Mapping;
using ExcelRenderer.Mapping.Configuration;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.TestFixtures;
using Xunit;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;
using static ExcelRenderer.Mapping.Tests.MappingFixture;

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
        using var result = Map(Fixture("MappingTests.Root_paths_and_optional_prefixes.5800487B05250F82.xlsx" , ("A1", expression)), """{"name":{"value":"Alice"}}""");
        Assert.Equal("Alice", Text(result, "A1"));
    }

    [Fact]
    public void Typed_values_formatting_dates_nulls_and_escaped_markers()
    {
        using var result = Map(Fixture("MappingTests.Typed_values_formatting_dates_nulls_and_escaped_markers.F3A9C970DD8BC8EC.xlsx"), """{"amount":1234.5,"date":"2026-10-09","enabled":true,"nothing":null,"key|with.pipe":7}""");
        var sheet = result;
        Assert.Equal(CellValueType.Number, Cell(sheet, "A1").Value.Type);
        Assert.Equal(1234.5, Number(sheet, "A1"));
        Assert.Equal("0.000", Style(sheet, "A1").NumberFormatCode);
        Assert.Equal("1,234.50", Text(sheet, "A2"));
        Assert.Equal("2026-10-09", Text(sheet, "A3"));
        Assert.Equal("2026/10/09", Text(sheet, "A4"));
        Assert.True(Cell(sheet, "A5").Value.Boolean == true);
        Assert.True(Cell(sheet, "A6").Value.Type == CellValueType.Blank);
        Assert.Equal("**literal", Text(sheet, "A7"));
        Assert.Equal("prefix **amount", Text(sheet, "A8"));
        Assert.Equal("007", Text(sheet, "A9"));
    }

    [Fact]
    public void Clr_objects_keep_dates_decimals_and_dictionary_keys()
    {
        using var input = Fixture("MappingTests.Clr_objects_keep_dates_decimals_and_dictionary_keys.FB58BB844F1CF45D.xlsx");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new
        {
            Date = new DateTime(2026, 10, 9),
            Amount = 12.345m,
            Map = new Dictionary<string, int[]> { ["a.b"] = [42] },
        });
        output.Position = 0;
        using var result = WorkbookProbe.Open(output);
        Assert.Equal(CellValueType.Number, Cell(result, "A1").Value.Type);
        Assert.Equal(14, Style(result, "A1").NumberFormatId);
        Assert.Equal(new DateTime(2026, 10, 9), Date(result, "A1"));
        Assert.Equal("12.35", Text(result, "A2"));
        Assert.Equal(42, Number(result, "A3"));
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public void Single_row_arrays_preserve_styles_merges_and_caller_totals()
    {
        using var result = Map(Fixture("MappingTests.Single_row_arrays_preserve_styles_merges_and_caller_totals.CEE3D6B7D3B87850.xlsx"), """{"items":[{"name":"A","quantity":2},{"name":"B","quantity":3}],"total":5}""");
        var sheet = result;
        Assert.Equal("A", Text(sheet, "A2"));
        Assert.Equal("B", Text(sheet, "A3"));
        Assert.Equal("Footer", Text(sheet, "A4"));
        Assert.Equal(32, Row(sheet, 3).HeightPt);
        Assert.True(Style(sheet, "C3").FontProperties.Bold == true);
        Assert.Equal("0.00", Style(sheet, "C3").NumberFormatCode);
        Assert.Contains(Info(sheet).MergedRanges, r => r.ToString() == "A3:B3");
        Assert.Equal(3, Number(sheet, "D3"));
        Assert.False(Cell(sheet, "D4").Formula != null);
        Assert.Equal(5, Number(sheet, "D4"));
        Assert.Equal("A1:D4", PrintArea(sheet));
        Assert.Contains("$A$4", Names(result).Single(n => n.Name == "FooterCell").RawText);
        Assert.Contains(2, Breaks(sheet));
        Assert.Contains(3, Breaks(sheet));
    }

    [Fact]
    public void Nested_blocks_expand_parent_aliases_empty_children_and_multiple_sheets()
    {
        using var result = Map(Fixture("MappingTests.Nested_blocks_expand_parent_aliases_empty_children_and_multiple_sheets.C94729B31B111424.xlsx"), """{"title":"Root","orders":[{"number":"O1","total":10,"lines":[{"name":"A"},{"name":"B"}]},{"number":"O2","total":0,"lines":[]}]}""");
        var s = result;
        Assert.Equal(new[] { "O1", "A", "B", "10", "O2", "0", "Footer" }, Enumerable.Range(1, 7).Select(r => Display(s, r, 1)).ToArray());
        Assert.Equal("O1", Text(s, "B3"));
        Assert.Equal("Root", Text(s, "C3"));
        Assert.Equal("Root", Text(result, "A1", 2));
        Assert.DoesNotContain(Cells(s), c => c.Value.Type == CellValueType.String && c.Value.Text!.StartsWith("**"));
    }

    [Fact]
    public void Empty_arrays_delete_single_rows_and_blocks()
    {
        using var result = Map(Fixture("MappingTests.Empty_arrays_delete_single_rows_and_blocks.90C9053C46D167E5.xlsx"), """{"items":[]}""");
        Assert.Equal("Header", Text(result, "A1"));
        Assert.Equal("Footer", Text(result, "A2"));
        Assert.Equal(2, LastContentRow(result));
    }

    [Fact]
    public void Page_breaks_follow_expansion_and_override_fit_mode()
    {
        using var result = Map(Fixture("MappingTests.Page_breaks_follow_expansion_and_override_fit_mode.5AA3B127EF880B40.xlsx"), """{"items":[1,2,3]}""");
        var s = result;
        Assert.Contains(3, Breaks(s));
        Assert.Contains(2, Breaks(s, true));
        Assert.True(Cell(s, "C4").Value.Type == CellValueType.Blank);
        Assert.Equal(100, int.Parse(Info(s).PageSetup!.Attributes["scale"]));
        Assert.Equal("0", Info(s).PageSetup!.Attributes.GetValueOrDefault("fitToWidth", "0"));
        Assert.Equal("0", Info(s).PageSetup!.Attributes.GetValueOrDefault("fitToHeight", "0"));
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
        using var input = Fixture("MappingTests.Invalid_mappings_report_original_cell_and_preserve_output.3FB64B7E410B7995.xlsx" , ("B7", expression));
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
        using var input = Fixture("MappingTests.Marker_errors_are_validated_even_for_empty_arrays.025127A42AD0C8D1.xlsx" , ("A1", first), ("A2", body), ("A3", last));
        using var output = new MemoryStream();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[]}"""));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Different_arrays_and_cross_boundary_merges_are_rejected()
    {
        using var input = Fixture("MappingTests.Different_arrays_and_cross_boundary_merges_are_rejected.E0F7FAF29B3848CD.xlsx");
        using var output = new MemoryStream();
        Assert.Contains("same array", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, "{}" )).Message);
        using var merged = Fixture("MappingTests.Different_arrays_and_cross_boundary_merges_are_rejected.18E0B653B73072A9.xlsx");
        Assert.Contains("boundary", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(merged, output, "{}" )).Message);
    }

    [Fact]
    public void Expansion_limit_and_cancellation_leave_destination_untouched()
    {
        using var input = Fixture("MappingTests.Expansion_limit_and_cancellation_leave_destination_untouched.4DE02E339C54AF2E.xlsx");
        using var output = new MemoryStream();
        Assert.Contains("MaxOutputRows", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(input, output, """{"items":[1,2,3]}""", new MappingOptions { MaxOutputRows = 2 })).Message);
        Assert.Empty(output.ToArray());
        input.Position = 0;
        Assert.Throws<OperationCanceledException>(() => ExcelTemplateMapper.MapJson(input, output, "{}", cancellationToken: new CancellationToken(true)));
    }


    [Fact]
    public void Multirow_blocks_preserve_vertical_merges_hidden_rows_and_validation()
    {
        using var result = Map(Fixture("MappingTests.Multirow_blocks_preserve_vertical_merges_hidden_rows_and_validation.79AF609BD99610AB.xlsx"), """{"items":[{"value":3},{"value":7}]}""");
        var sheet = result;
        Assert.Equal(3, Number(sheet, "A1"));
        Assert.Equal(7, Number(sheet, "A3"));
        Assert.Equal(3, Number(sheet, "A2"));
        Assert.Equal(7, Number(sheet, "A4"));
        Assert.Equal("Footer", Text(sheet, "A5"));
        Assert.True(Row(sheet, 2).Hidden == true);
        Assert.True(Row(sheet, 4).Hidden == true);
        Assert.Contains(Info(sheet).MergedRanges, r => r.ToString() == "B1:B2");
        Assert.Contains(Info(sheet).MergedRanges, r => r.ToString() == "B3:B4");
        Assert.Contains(Xml(sheet).Descendants<DocumentFormat.OpenXml.Spreadsheet.DataValidation>(), v => v.SequenceOfReferences!.InnerText!.Split(' ').Any(r => r == "A3" || r == "A3:A3"));
        Assert.Contains(Info(sheet).ConditionalFormattingRanges.SelectMany(ranges => ranges), range => range.First.Row <= 3 && range.Last.Row >= 3);
    }

    [Fact]
    public void Marker_content_alias_shadowing_nonarrays_and_objects_are_rejected()
    {
        using var marker = Fixture("MappingTests.Marker_content_alias_shadowing_nonarrays_and_objects_are_rejected.ACABF8133EACAA76.xlsx");
        using var output = new MemoryStream();
        Assert.Contains("dedicated", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(marker, output, "{}" )).Message);
        using var shadow = Fixture("MappingTests.Marker_content_alias_shadowing_nonarrays_and_objects_are_rejected.DC9781F92BC9F1E0.xlsx");
        Assert.Contains("shadow", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(shadow, output, "{}" )).Message);
        using var array = Fixture("MappingTests.Marker_content_alias_shadowing_nonarrays_and_objects_are_rejected.4DE02E339C54AF2E.xlsx");
        Assert.Contains("array", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(array, output, """{"items":null}""" )).Message);
        using var value = Fixture("MappingTests.Marker_content_alias_shadowing_nonarrays_and_objects_are_rejected.99F0B5FA999D0F59.xlsx");
        Assert.Contains("scalar", Assert.Throws<MappingException>(() => ExcelTemplateMapper.MapJson(value, output, """{"items":{}}""" )).Message);
    }

    [Fact]
    public void Root_scalar_array_indices_and_readonly_dictionaries_are_supported()
    {
        using var json = Map(Fixture("MappingTests.Root_scalar_array_indices_and_readonly_dictionaries_are_supported.3D6A9CD927BB6096.xlsx"), """{"items":[1,2]}""");
        Assert.Equal(2, Number(json, "A1"));
        using var scalar = Map(Fixture("MappingTests.Root_scalar_array_indices_and_readonly_dictionaries_are_supported.1AFA118344DFB67D.xlsx"), "42");
        Assert.Equal(42, Number(scalar, "A1"));
        using var input = Fixture("MappingTests.Root_scalar_array_indices_and_readonly_dictionaries_are_supported.5CB53AA5441CBC44.xlsx");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["value"] = 7 }));
        output.Position = 0;
        using var mapped = WorkbookProbe.Open(output);
        Assert.Equal(7, Number(mapped, "A1"));
    }


    [Fact]
    public void Iso_offsets_are_preserved_and_date_conversion_is_explicit()
    {
        using var result = Map(Fixture("MappingTests.Iso_offsets_are_preserved_and_date_conversion_is_explicit.2EDEC375BAE461EA.xlsx"), """{"date":"2026-10-09T01:02:03+09:00"}""");
        Assert.Equal("2026-10-09 01:02 +09:00", Text(result, "A1"));
        Assert.Equal("2026-10-09T01:02:03+09:00", Text(result, "A2"));
    }

    [Fact]
    public void Dictionary_paths_remain_case_sensitive_even_with_insensitive_comparer()
    {
        using var input = Fixture("MappingTests.Dictionary_paths_remain_case_sensitive_even_with_insensitive_comparer.DC1490280402EA16.xlsx");
        using var output = new MemoryStream();
        var data = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["name"] = 42 };
        Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, data));
    }

    private static WorkbookProbe Map(Stream input, string json)
    {
        using var output = new MemoryStream();
        ExcelTemplateMapper.MapJson(input, output, json);
        output.Position = 0;
        return WorkbookProbe.Open(output);
    }
}
