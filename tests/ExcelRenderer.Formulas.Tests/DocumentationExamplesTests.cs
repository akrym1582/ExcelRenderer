using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class DocumentationExamplesTests
{
    public static IEnumerable<object[]> Examples()
    {
        foreach (var document in new[] { "formulas-reference.md", "formulas-reference.ja.md" })
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Documentation", document)).ReplaceLineEndings("\n");
            var functions = Regex.Matches(text, "^\\| `(?<name>[A-Z]+)` \\|.*\\| `=(?<formula>[^`]+)` → (?<expected>[^|]+)\\|$", RegexOptions.Multiline);
            foreach (Match match in functions)
            {
                yield return new object[] { document + ": " + match.Groups["name"].Value, match.Groups["formula"].Value, match.Groups["expected"].Value.Trim() };
            }

            var formats = Regex.Matches(text, "^\\|[^|]+\\| `=(?<formula>TEXT[^`]+)` \\| `(?<expected>[^`]+)` \\|$", RegexOptions.Multiline);
            foreach (Match match in formats)
            {
                yield return new object[] { document + ": TEXT format", match.Groups["formula"].Value, match.Groups["expected"].Value };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void ReferenceExamplesMatchPublishedResults(string context, string formula, string expected)
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Examples");
        for (var row = 1; row <= 4; row++)
        {
            builder.SetValue(new FormulaCellAddress("Examples", row, 1), FormulaValue.FromText(row == 2 ? "Travel" : "Office"));
            builder.SetValue(new FormulaCellAddress("Examples", row, 2), FormulaValue.FromNumber(row * 10));
            builder.SetValue(new FormulaCellAddress("Examples", row, 3), FormulaValue.FromBoolean(row != 3));
        }

        var parsed = FormulaParser.Parse(formula);
        Assert.True(parsed.Success, context + ": " + formula);
        var result = new FormulaEngine().Evaluate(parsed.Formula!, builder.Build(), new FormulaCellAddress("Examples", 10, 4));
        Assert.True(result.Success, context + ": " + string.Join("; ", result.Diagnostics));
        if (expected.StartsWith('"'))
        {
            Assert.Equal(FormulaValueKind.Text, result.Value.Kind);
            Assert.Equal(JsonSerializer.Deserialize<string>(expected), result.Value.Text);
        }
        else if (expected is "TRUE" or "FALSE")
        {
            Assert.Equal(FormulaValueKind.Boolean, result.Value.Kind);
            Assert.Equal(expected == "TRUE", result.Value.Boolean);
        }
        else
        {
            Assert.Equal(FormulaValueKind.Number, result.Value.Kind);
            Assert.Equal(double.Parse(expected, CultureInfo.InvariantCulture), result.Value.Number);
        }
    }
}
