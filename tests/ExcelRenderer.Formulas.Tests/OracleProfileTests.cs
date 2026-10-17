using System.Globalization;
using System.Text.Json;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class OracleProfileTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Oracle", "cases.json")));
        return document.RootElement.EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()!, c.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitProfileCases(string id, JsonElement item)
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Case");
        foreach (var input in item.GetProperty("inputs").EnumerateArray())
        {
            var address = ParseAddress(input.GetProperty("address").GetString()!);
            var kind = input.GetProperty("kind").GetString();
            var value = input.GetProperty("value");
            if (kind == "Formula") builder.SetFormula(address, value.GetString()!);
            else builder.SetValue(address, kind switch
            {
                "Number" => FormulaValue.FromNumber(value.GetDouble()),
                "Text" => FormulaValue.FromText(value.GetString()!),
                "Boolean" => FormulaValue.FromBoolean(value.GetBoolean()),
                "Error" => FormulaValue.FromError(value.GetString()!),
                _ => FormulaValue.Blank,
            });
        }

        var formula = item.GetProperty("formula").GetString()!;
        var parsed = FormulaParser.Parse(formula);
        Assert.True(parsed.Success, id + ": " + formula);
        var result = new FormulaEngine().Evaluate(parsed.Formula!, builder.Build(), new("Case", 1, 26), new() { DateSystem = Enum.Parse<FormulaDateSystem>(item.GetProperty("dateSystem").GetString()!) });
        var expected = item.GetProperty("expected");
        if (expected.GetProperty("kind").GetString() == "Diagnostic")
        {
            Assert.False(result.Success);
            Assert.Equal(expected.GetProperty("value").GetString(), result.Diagnostics[0].Code);
            return;
        }

        Assert.True(result.Success, id + ": " + string.Join(";", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(expected.GetProperty("kind").GetString(), result.Value.Kind.ToString());
        var payload = expected.GetProperty("value");
        switch (result.Value.Kind)
        {
            case FormulaValueKind.Number: Assert.Equal(payload.GetDouble(), result.Value.Number); break;
            case FormulaValueKind.Boolean: Assert.Equal(payload.GetBoolean(), result.Value.Boolean); break;
            case FormulaValueKind.Text: case FormulaValueKind.Error: Assert.Equal(payload.GetString(), result.Value.Text); break;
        }
    }

    private static FormulaCellAddress ParseAddress(string address)
    {
        int column = 0, index = 0;
        while (index < address.Length && char.IsLetter(address[index])) column = column * 26 + address[index++] - 'A' + 1;
        return new("Case", int.Parse(address[index..], CultureInfo.InvariantCulture), column);
    }
}
