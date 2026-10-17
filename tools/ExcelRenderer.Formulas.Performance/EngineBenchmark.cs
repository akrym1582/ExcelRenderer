using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.Mapping;
using ExcelRenderer.OpenXml.Calculation;

/// <summary>Measures formula sessions and transactional workbook integration.</summary>
internal static class EngineBenchmark
{
    /// <summary>Runs a selected benchmark family.</summary>
    /// <param name="mode">Family name.</param>
    /// <param name="count">Detail or cell count.</param>
    /// <param name="bytes">Prepared OOXML fixture.</param>
    internal static void Run(string mode, int count, byte[] bytes)
    {
        if (mode == "rounding")
        {
            var workbook = new InMemoryFormulaWorkbook.Builder().AddSheet("Data").Build();
            foreach (var (formula, expected) in new[] { ("ROUND(12.345,2)", 12.35), ("ROUNDUP(12.341,2)", 12.35), ("ROUNDDOWN(12.349,2)", 12.34) })
            {
                var parsed = FormulaParser.Parse(formula).Formula!;
                Benchmark.Measure("engine-decimal-" + parsed.Root.Text.ToLowerInvariant(), count, () =>
                {
                    var session = new FormulaEngine().CreateSession(workbook);
                    double sum = 0;
                    for (var iteration = 0; iteration < count; iteration++)
                    {
                        sum += Number(session.Evaluate(parsed, new FormulaCellAddress("Data", 1, 1)), expected);
                    }

                    return sum;
                });
            }
        }
        else if (mode == "engine")
        {
            var constantWorkbook = new InMemoryFormulaWorkbook.Builder().AddSheet("Data").Build();
            var parsed = FormulaParser.Parse("ROUND(2*10*1.1,2)").Formula!;
            Benchmark.Measure("engine-constant-repeated", count, () =>
            {
                var session = new FormulaEngine().CreateSession(constantWorkbook);
                double sum = 0;
                for (var iteration = 0; iteration < count; iteration++)
                {
                    sum += Number(session.Evaluate(parsed, new FormulaCellAddress("Data", 1, 1)), 22);
                }

                return sum;
            });
            foreach (var shared in new[] { false, true })
            {
                var builder = new InMemoryFormulaWorkbook.Builder();
                builder.AddSheet("Data");
                builder.SetFormula(new FormulaCellAddress("Data", 1, 2), "2*10");
                for (var row = 1; row <= count; row++)
                {
                    builder.SetFormula(new FormulaCellAddress("Data", row, 1), shared ? "$B$1+2" : "ROUND(2*10*1.1,2)");
                }

                var workbook = builder.Build();
                Benchmark.Measure(shared ? "engine-common-dependency" : "engine-independent", count, () =>
                {
                    var session = new FormulaEngine().CreateSession(workbook);
                    double sum = 0;
                    for (var row = 1; row <= count; row++)
                    {
                        sum += Number(session.EvaluateCell(new FormulaCellAddress("Data", row, 1)), 22);
                    }

                    return sum;
                });
            }
        }
        else if (mode == "sum")
        {
            var builder = new InMemoryFormulaWorkbook.Builder();
            builder.AddSheet("Data");
            for (var row = 1; row <= count; row++)
            {
                builder.SetValue(new FormulaCellAddress("Data", row, 1), FormulaValue.FromNumber(2));
            }

            builder.SetFormula(new FormulaCellAddress("Data", 1, 2), "SUM(A1:A" + count + ")");
            var workbook = builder.Build();
            Benchmark.Measure("engine-sum", count, () => Number(new FormulaEngine().EvaluateCell(new FormulaCellAddress("Data", 1, 2), workbook), 2d * count));
        }
        else if (mode == "recalculate")
        {
            Benchmark.Measure("xlsx-recalculate-save", count, () =>
            {
                using var input = new MemoryStream(bytes, false);
                using var output = new MemoryStream();
                WorkbookRecalculator.Recalculate(input, output);
                return CheckSaved(output, count);
            });
        }
        else if (mode == "mapping" || mode == "mapping-no-recalculate")
        {
            var data = new { Items = Enumerable.Range(0, count).Select(_ => new { Quantity = 2 }).ToArray() };
            Benchmark.Measure(mode == "mapping" ? "mapping-expand-recalculate-save" : "mapping-expand-invalidate-save", count, () =>
            {
                using var input = new MemoryStream(bytes, false);
                using var output = new MemoryStream();
                ExcelTemplateMapper.Map(input, output, data, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate, RecalculateMappedWorkbook = mode == "mapping" });
                return CheckSaved(output, count, mode == "mapping");
            });
        }
        else
        {
            throw new ArgumentException("Unknown mode " + mode);
        }
    }

    private static double Number(FormulaEvaluationResult result, double expected)
    {
        if (!result.Success || result.Value.Kind != FormulaValueKind.Number || result.Value.Number != expected)
        {
            throw new InvalidOperationException("Formula mismatch: " + result.Value + "; " + string.Join(",", result.Diagnostics.Select(d => d.Message)));
        }

        return result.Value.Number;
    }

    private static double CheckSaved(MemoryStream output, int count, bool calculated = true)
    {
        output.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Read, true);
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var document = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = document.Descendants(ns + "c").Where(c => c.Element(ns + "f") != null).ToArray();
        if (cells.Length != count + 1)
        {
            throw new InvalidOperationException("Formula cell count mismatch: " + cells.Length);
        }

        foreach (var cell in cells)
        {
            if (!calculated)
            {
                if (cell.Element(ns + "v") != null)
                {
                    throw new InvalidOperationException("Unevaluated cache was retained.");
                }

                continue;
            }

            var cellType = (string?)cell.Attribute("t");
            if (cellType != null && cellType != "n")
            {
                throw new InvalidOperationException("Expected numeric cache type, got " + cellType);
            }

            var expected = (string?)cell.Attribute("r") == "B" + (count + 1) ? 22d * count : 22;
            var actual = double.Parse(cell.Element(ns + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture);
            if (actual != expected)
            {
                throw new InvalidOperationException("Saved cache mismatch at " + cell.Attribute("r") + ": " + actual + " vs " + expected);
            }
        }

        return 22d * count;
    }
}
