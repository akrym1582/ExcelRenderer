using System.Globalization;
using System.Text.Json;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Reading;

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
    if (mapping)
    {
        var mappingId = item.GetProperty("id").GetString()!;
        var target = Address(item.GetProperty("target").GetString()!);
        using var mappingInput = File.OpenRead(Path.Combine(args[2], mappingId + ".xlsx"));
        using var mappingReader = OpenXmlWorkbookReader.Open(mappingInput);
        var mappingSession = WorkbookCalculationSession.Create(mappingReader);
        var mappingCell = mappingReader.EnumerateCells(1).Single(c => c.Address.Row == target.Row && c.Address.Column == target.Column);
        results.Add(new { id = mappingId, pipeline = "mapping", actual = CellPayload(mappingSession.EvaluateCell(mappingCell).Value) });
        Directory.CreateDirectory(args[3]);
        mappingInput.Position = 0;
        var savedPath = Path.Combine(args[3], mappingId + ".xlsx");
        using (var output = File.Create(savedPath))
        {
            WorkbookRecalculator.Recalculate(mappingInput, output);
        }

        using var mappingSaved = File.OpenRead(savedPath);
        using var mappingReread = OpenXmlWorkbookReader.Open(mappingSaved);
        var mappingFinal = mappingReread.EnumerateCells(1).Single(c => c.Address.Row == target.Row && c.Address.Column == target.Column);
        results.Add(new { id = mappingId, pipeline = "roundtrip", actual = CellPayload(mappingFinal.Value) });
        continue;
    }

    var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Case");
    foreach (var input in item.GetProperty("inputs").EnumerateArray())
    {
        var address = Address(input.GetProperty("address").GetString()!);
        var payload = input.GetProperty("value");
        var kind = input.GetProperty("kind").GetString();
        if (kind == "Formula")
        {
            builder.SetFormula(address, payload.GetString()!);
        }
        else
        {
            var typed = kind switch
            {
                "Number" => FormulaValue.FromNumber(payload.GetDouble()),
                "Text" => FormulaValue.FromText(payload.GetString()!),
                "Boolean" => FormulaValue.FromBoolean(payload.GetBoolean()),
                "Error" => FormulaValue.FromError(payload.GetString()!),
                _ => FormulaValue.Blank,
            };
            builder.SetValue(address, typed);
        }
    }

    var id = item.GetProperty("id").GetString()!;
    var formula = item.GetProperty("formula").GetString()!;
    builder.SetFormula(Address("Z1"), formula);
    var options = new FormulaEvaluationOptions { DateSystem = Enum.Parse<FormulaDateSystem>(item.GetProperty("dateSystem").GetString()!) };
    var result = new FormulaEngine().EvaluateCell(Address("Z1"), builder.Build(), options);
    results.Add(new { id, pipeline = "engine", actual = Payload(result) });
    if (args.Length >= 3)
    {
        using var input = File.OpenRead(Path.Combine(args[2], id + ".xlsx"));
        using var reader = OpenXmlWorkbookReader.Open(input);
        try
        {
            var session = WorkbookCalculationSession.Create(reader, options);
            var cell = reader.EnumerateCells(1).Single(c => c.Address.Row == 1 && c.Address.Column == 26);
            var evaluated = session.EvaluateCell(cell);
            results.Add(new { id, pipeline = "openxml", actual = CellPayload(evaluated.Value) });
            if (args.Length == 4)
            {
                Directory.CreateDirectory(args[3]);
                input.Position = 0;
                var path = Path.Combine(args[3], id + ".xlsx");
                using (var output = File.Create(path))
                {
                    WorkbookRecalculator.Recalculate(input, output, options: options);
                }

                using var saved = File.OpenRead(path);
                using var reread = OpenXmlWorkbookReader.Open(saved);
                var final = reread.EnumerateCells(1).Single(c => c.Address.Row == 1 && c.Address.Column == 26);
                results.Add(new { id, pipeline = "roundtrip", actual = CellPayload(final.Value) });
            }
        }
        catch (FormulaCalculationException ex)
        {
            results.Add(new { id, pipeline = "openxml", actual = new { kind = "Diagnostic", value = ex.Message } });
            if (args.Length == 4)
            {
                results.Add(new { id, pipeline = "roundtrip", actual = new { kind = "Diagnostic", value = ex.Message } });
            }
        }
    }
}

File.WriteAllText(args[1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Evaluated {results.Count} pipeline cases. Excel observations are compared separately.");
return 0;

static FormulaCellAddress Address(string text)
{
    int column = 0;
    int i = 0;
    while (i < text.Length && char.IsLetter(text[i]))
    {
        column = (column * 26) + text[i++] - 'A' + 1;
    }

    return new FormulaCellAddress("Case", int.Parse(text.Substring(i), CultureInfo.InvariantCulture), column);
}

static object Payload(FormulaEvaluationResult result) => !result.Success
    ? new { kind = "Diagnostic", value = (object?)result.Diagnostics[0].Code }
    : new { kind = result.Value.Kind.ToString(), value = ValuePayload(result.Value) };

static object? ValuePayload(FormulaValue value) => value.Kind switch
{
    FormulaValueKind.Number => value.Number,
    FormulaValueKind.Boolean => value.Boolean,
    FormulaValueKind.Text or FormulaValueKind.Error => value.Text,
    _ => null,
};

static object CellPayload(CellValue value) => value.Type switch
{
    CellValueType.Number => new { kind = "Number", value = (object?)value.Number },
    CellValueType.Boolean => new { kind = "Boolean", value = (object?)value.Boolean },
    CellValueType.String => new { kind = "Text", value = (object?)(value.Text ?? string.Empty) },
    CellValueType.Error => new { kind = "Error", value = (object?)value.Text },
    _ => new { kind = "Blank", value = (object?)null },
};
