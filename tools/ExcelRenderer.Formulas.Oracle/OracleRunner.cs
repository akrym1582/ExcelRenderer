using System.Globalization;
using System.Text.Json;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Model.Cells;

namespace ExcelRenderer.Formulas.Oracle;

/// <summary>Executes the non-Excel oracle pipelines.</summary>
internal static class OracleRunner
{
    /// <summary>Runs the command line tool.</summary>
    /// <param name="args">Compatible command arguments.</param>
    /// <returns>Exit code.</returns>
    internal static int Run(string[] args)
    {
        var mapping = args.Length > 0 && args[0] == "--mapping";
        if (mapping)
        {
            args = args.Skip(1).ToArray();
        }

        if (args.Length is < 2 or > 4 || (mapping && args.Length != 4))
        {
            Console.Error.WriteLine("Usage: oracle cases.json actual.json [generated-workbook-directory] [roundtrip-output-directory]");
            return 2;
        }

        using var cases = JsonDocument.Parse(File.ReadAllText(args[0]));
        var results = new List<object>();
        foreach (var item in cases.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var targetText = item.GetProperty("target").GetString()!;
            var target = Address(id, targetText);
            foreach (var input in item.GetProperty("inputs").EnumerateArray())
            {
                if (Address(id, input.GetProperty("address").GetString()!).Equals(target))
                {
                    throw new InvalidDataException($"Case '{id}', target '{targetText}' overlaps inputs.");
                }
            }

            var options = new FormulaEvaluationOptions { DateSystem = Enum.Parse<FormulaDateSystem>(item.GetProperty("dateSystem").GetString()!) };
            var stages = new List<(string Pipeline, Func<object> Execute)>();
            if (!mapping)
            {
                stages.Add(("engine", () =>
                {
                    var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Case");
                    foreach (var input in item.GetProperty("inputs").EnumerateArray())
                    {
                        var address = Address(id, input.GetProperty("address").GetString()!);
                        var value = input.GetProperty("value");
                        var kind = input.GetProperty("kind").GetString();
                        if (kind == "Formula")
                        {
                            builder.SetFormula(address, value.GetString()!);
                        }
                        else
                        {
                            var typed = kind switch
                            {
                                "Number" => FormulaValue.FromNumber(value.GetDouble()),
                                "Text" => FormulaValue.FromText(value.GetString()!),
                                "Boolean" => FormulaValue.FromBoolean(value.GetBoolean()),
                                "Error" => FormulaValue.FromError(value.GetString()!),
                                _ => FormulaValue.Blank,
                            };
                            builder.SetValue(address, typed);
                        }
                    }

                    builder.SetFormula(target, item.GetProperty("formula").GetString()!);
                    return Payload(new FormulaEngine().EvaluateCell(target, builder.Build(), options));
                }));
            }

            if (args.Length >= 3)
            {
                stages.Add((mapping ? "mapping" : "openxml", () =>
                {
                    using var input = File.OpenRead(Path.Combine(args[2], id + ".xlsx"));
                    using var reader = OpenXmlWorkbookReader.Open(input);
                    var cell = TargetCell(reader, id, targetText, target);
                    return CellPayload(WorkbookCalculationSession.Create(reader, options).EvaluateCell(cell).Value);
                }));
            }

            if (args.Length == 4)
            {
                stages.Add(("roundtrip", () =>
                {
                    Directory.CreateDirectory(args[3]);
                    var path = Path.Combine(args[3], id + ".xlsx");
                    using (var input = File.OpenRead(Path.Combine(args[2], id + ".xlsx")))
                    using (var output = File.Create(path))
                    {
                        WorkbookRecalculator.Recalculate(input, output, options: options);
                    }

                    using var saved = File.OpenRead(path);
                    using var reader = OpenXmlWorkbookReader.Open(saved);
                    return CellPayload(TargetCell(reader, id, targetText, target).Value);
                }));
            }

            results.AddRange(ExecuteStages(id, stages));
        }

        File.WriteAllText(args[1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Evaluated {results.Count} pipeline cases. Excel observations are compared separately.");
        return 0;
    }

    /// <summary>Runs requested stages independently, retaining exactly one result per stage.</summary>
    /// <param name="id">Case identifier.</param>
    /// <param name="stages">Ordered pipeline operations.</param>
    /// <returns>Recorded results.</returns>
    internal static IReadOnlyList<object> ExecuteStages(string id, IEnumerable<(string Pipeline, Func<object> Execute)> stages)
    {
        var results = new List<object>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pipeline, execute) in stages)
        {
            if (!seen.Add(pipeline))
            {
                throw new InvalidDataException($"Case '{id}': duplicate pipeline '{pipeline}'.");
            }

            object actual;
            try
            {
                actual = execute();
            }
            catch (FormulaCalculationException ex)
            {
                actual = new { kind = "Diagnostic", value = ex.Message };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Case '{id}', pipeline '{pipeline}': {ex.Message}", ex);
            }

            results.Add(new { id, pipeline, actual });
        }

        return results;
    }

    private static FormulaCellAddress Address(string id, string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\A([A-Z]{1,3})([1-9][0-9]{0,6})\z");
        int column = 0;
        foreach (var letter in match.Groups[1].Value)
        {
            column = (column * 26) + letter - 'A' + 1;
        }

        if (!match.Success || column > 16384 || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row > 1048576)
        {
            throw new InvalidDataException($"Case '{id}': invalid target/address '{text}'.");
        }

        return new FormulaCellAddress("Case", row, column);
    }

    private static CellRecord TargetCell(OpenXmlWorkbookReader reader, string id, string text, FormulaCellAddress target)
    {
        return reader.EnumerateCells(1).SingleOrDefault(c => c.Address.Row == target.Row && c.Address.Column == target.Column)
            ?? throw new InvalidDataException($"Case '{id}': target '{text}' does not exist.");
    }

    private static object Payload(FormulaEvaluationResult result) => !result.Success
        ? new { kind = "Diagnostic", value = (object?)result.Diagnostics[0].Code }
        : new { kind = result.Value.Kind.ToString(), value = ValuePayload(result.Value) };

    private static object? ValuePayload(FormulaValue value) => value.Kind switch
    {
        FormulaValueKind.Number => value.Number,
        FormulaValueKind.Boolean => value.Boolean,
        FormulaValueKind.Text or FormulaValueKind.Error => value.Text,
        _ => null,
    };

    private static object CellPayload(CellValue value) => value.Type switch
    {
        CellValueType.Number => new { kind = "Number", value = (object?)value.Number },
        CellValueType.Boolean => new { kind = "Boolean", value = (object?)value.Boolean },
        CellValueType.String => new { kind = "Text", value = (object?)(value.Text ?? string.Empty) },
        CellValueType.Error => new { kind = "Error", value = (object?)value.Text },
        _ => new { kind = "Blank", value = (object?)null },
    };
}
