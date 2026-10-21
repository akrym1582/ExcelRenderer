using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed partial class ConditionalFormattingTests
{
    [Fact]
    public void Duplicate_sqrefs_spend_only_the_unique_display_candidate_budget()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>2</v></c></row><row r=\"25000\"><c r=\"A25000\"/></row>",
            Rule("containsBlanks", "", ranges: string.Join(" ", Enumerable.Repeat("A1:A25000", 100))));
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 25000, 1) }).Sheets[0];
            Assert.DoesNotContain(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
            Assert.Null(sheet.Cells[new(1, 1)].Style.Background);
            Assert.NotNull(sheet.Cells[new(24999, 1)].Style.Background);
            Assert.Equal(25000, metrics["conditionalFormatting.displayCandidateVisits"]);
            Assert.Equal(25000, metrics["conditionalFormatting.candidates"]);
            Assert.Equal(1, metrics["conditionalFormatting.ruleRowPlans"]);
            Assert.Equal(25000, metrics["conditionalFormatting.evaluations"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Overlapping_rules_share_ordered_bands_and_evaluate_each_rule_once_per_cell()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row><row r=\"4\"><c r=\"D4\"><v>1</v></c></row>",
            Rule("containsBlanks", "", ranges: "A1:C3 B2:D4 D4") + Rule("expression", "<formula>TRUE</formula>", priority: 2, dxf: 1, ranges: "D1:D4"), [Yellow, Red]);
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var sheet = Read(input);
            Assert.Equal(15, metrics["conditionalFormatting.displayCandidateVisits"]);
            Assert.Equal(18, metrics["conditionalFormatting.evaluations"]);
            Assert.Equal(3, metrics["conditionalFormatting.ruleRowPlans"]);
            Assert.Equal(1, metrics["conditionalFormatting.rulePrioritySorts"]);
            Assert.NotNull(sheet.Cells[new(2, 4)].Style.Background);
            Assert.Equal(new Model.ReportColor(255, 0, 0), sheet.Cells[new(2, 4)].Style.Font.Color);
            Assert.Null(sheet.Cells[new(4, 4)].Style.Background);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Adjacent_mapping_style_sqrefs_share_one_rule_band_and_keep_the_original_formula_anchor()
    {
        var rows = string.Concat(Enumerable.Range(1, 1000).Select(row => $"<row r=\"{row}\"><c r=\"A{row}\"><v>{row}</v></c></row>"));
        var ranges = string.Join(" ", Enumerable.Range(1, 1000).Reverse().Select(row => "A" + row));
        using var input = Book(rows, Rule("expression", "<formula>A1000&gt;500</formula>", ranges: ranges));
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var sheet = Read(input);
            Assert.Null(sheet.Cells[new(500, 1)].Style.Background);
            Assert.NotNull(sheet.Cells[new(501, 1)].Style.Background);
            Assert.Equal(1, metrics["conditionalFormatting.ruleRowPlans"]);
            Assert.Equal(1, metrics["conditionalFormatting.ruleListSearches"]);
            Assert.Equal(1000, metrics["conditionalFormatting.evaluations"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Union_limit_diagnostic_finds_a_visible_target_after_a_hidden_first_row()
    {
        using var input = Book("<row r=\"1\" hidden=\"1\"><c r=\"A1\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\"><v>2</v></c></row>", Rule("expression", "<formula>TRUE</formula>", ranges: "A1:ALL2001"));
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 2001, 1000) }).Sheets[0];
        var diagnostic = Assert.Single(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
        Assert.Equal("A2", diagnostic.CellRange);
        Assert.All(sheet.Cells.Values, cell => Assert.Null(cell.Style.Background));
    }

    [Fact]
    public void Union_limit_diagnostic_finds_a_visible_target_after_two_million_hidden_coordinates()
    {
        var rows = string.Concat(Enumerable.Range(1, 2000).Select(row => $"<row r=\"{row}\" hidden=\"1\"/>")) + "<row r=\"2001\"><c r=\"A2001\"><v>2</v></c></row>";
        using var input = Book(rows, Rule("expression", "<formula>TRUE</formula>", ranges: "A1:ALL2001"));
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 2001, 1000) }).Sheets[0];
        var diagnostic = Assert.Single(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
        Assert.Equal("A2001", diagnostic.CellRange);
        Assert.All(sheet.Cells.Values, cell => Assert.Null(cell.Style.Background));
    }

    [Fact]
    public void Excess_unique_union_is_rejected_before_any_style_is_applied()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"B1\"><v>2</v></c></row>", Rule("expression", "<formula>TRUE</formula>", ranges: "A1:ALL2001"));
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 2001, 1000) }).Sheets[0];
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
        Assert.All(sheet.Cells.Values, cell => Assert.Null(cell.Style.Background));
        Assert.Equal(2, sheet.Cells.Count);
    }
}
