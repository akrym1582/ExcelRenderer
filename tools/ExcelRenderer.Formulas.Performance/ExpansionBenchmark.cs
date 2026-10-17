using System.Diagnostics;
using System.Text.Json;
using ExcelRenderer.Formulas;

/// <summary>Measures identical expanded workloads against the baseline and current engine.</summary>
internal static class ExpansionBenchmark
{
    /// <summary>Runs sparse, criteria, lookup, text/date and index construction workloads.</summary>
    /// <param name="mode">Workload family.</param>
    /// <param name="count">Stored cell count.</param>
    internal static void Run(string mode, int count)
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Data").AddSheet("Report").AddSheet("Empty");
        for (var i = 1; i <= count; i++)
        {
            builder.SetValue(new FormulaCellAddress("Data", i == count ? 1048576 : i, 1), FormulaValue.FromNumber(i));
            builder.SetValue(new FormulaCellAddress("Data", i == count ? 1048576 : i, 2), FormulaValue.FromNumber(2));
        }

        if (mode == "expansion-sparse")
        {
            Measure("index-build", count, () =>
            {
                var snapshot = builder.Build();
                var session = new FormulaEngine().CreateSession(snapshot);
                return (session, session.EvaluateCell(new FormulaCellAddress("Data", 1048576, 1)), (double)count);
            });
        }

        var workbook = builder.Build();
        var formulas = mode.StartsWith("expansion-lookup", StringComparison.Ordinal)
            ? new[] { ("VLOOKUP(" + count + ",Data!A:B,2,FALSE)", 1), ("VLOOKUP(" + count + ",Data!A:B,2,FALSE)", 10), ("VLOOKUP(" + count + ",Data!A:B,2,FALSE)", 100) }
            : mode == "expansion-text-date"
                ? new[] { ("LEN(TEXT(EOMONTH(DATE(2026,1,31),1),\"yyyy-mm-dd\"))", 1000) }
                : mode == "expansion-mixed"
                    ? new[] { ("SUMIFS(Data!B:B,Data!A:A,\">0\")+LEN(TEXT(EOMONTH(DATE(2026,1,31),1),\"yyyy-mm-dd\"))+VLOOKUP(" + count + ",Data!A:B,2,FALSE)", 1) }
                    : new[] { ("SUM(Data!A:A)", 1), ("COUNTIF(Empty!A1:A1000,\"\")", 1), ("COUNTIF(Empty!A:A,\"\")", 1), ("SUMIFS(Data!B:B,Data!A:A,\">0\")", 1), ("COUNTIFS(Empty!A2:A1000000,\"\",Empty!B9:B1000007,\"\")", 1) };
        foreach (var (text, repeats) in formulas)
        {
            var parsed = FormulaParser.Parse(text).Formula!;
            Measure(text + (mode == "expansion-lookup-varied" ? " varied" : string.Empty) + " x" + repeats, count, () =>
            {
                var limits = new FormulaEvaluationOptions();
                if (mode.StartsWith("expansion-lookup", StringComparison.Ordinal))
                {
                    limits.MaxCellVisits = 10000000;
                    limits.MaxEvaluationSteps = 50000000;
                }

                var session = new FormulaEngine().CreateSession(workbook, limits);
                FormulaEvaluationResult result = null!;
                double checksum = 0;
                for (var i = 0; i < repeats; i++)
                {
                    var query = mode == "expansion-lookup-varied" ? FormulaParser.Parse("VLOOKUP(" + (count - ((i * 137) % count)) + ",Data!A:B,2,FALSE)").Formula! : parsed;
                    result = session.Evaluate(query, new FormulaCellAddress("Report", 1, 1));
                    if (!result.Success)
                    {
                        break;
                    }

                    checksum += result.Value.Number;
                }

                #if !EXPANSION_BASELINE
                var expected = mode == "expansion-mixed" ? (2d * count) + 12 : mode.StartsWith("expansion-lookup", StringComparison.Ordinal) ? 2d * repeats : mode == "expansion-text-date" ? 10d * repeats : text.StartsWith("SUMIFS", StringComparison.Ordinal) ? 2d * count : text.StartsWith("SUM", StringComparison.Ordinal) ? count * (count + 1d) / 2 : text.StartsWith("COUNTIFS", StringComparison.Ordinal) ? 999999 : text.Contains("A1:A1000", StringComparison.Ordinal) ? 1000 : 1048576;
                if (!result.Success || checksum != expected)
                {
                    throw new InvalidOperationException("Benchmark result mismatch: " + text);
                }
#endif
                return (session, result, checksum);
            });
        }
    }

    private static void Measure(string name, int count, Func<(FormulaSession Session, FormulaEvaluationResult Result, double Checksum)> operation)
    {
        for (var iteration = 0; iteration < 6; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            var timer = Stopwatch.StartNew();
            var (session, result, checksum) = operation();
            timer.Stop();
            var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
            object? statistics = null;
#if !EXPANSION_BASELINE
            statistics = session.GetStatistics();
#endif
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Name = name,
                Count = count,
                Iteration = iteration,
                Warmup = iteration == 0,
                Milliseconds = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = bytes,
                Gen0 = GC.CollectionCount(0) - collections[0],
                Gen1 = GC.CollectionCount(1) - collections[1],
                Gen2 = GC.CollectionCount(2) - collections[2],
                Success = result.Success,
                Kind = result.Value.Kind.ToString(),
                Checksum = result.Success ? checksum : (double?)null,
                Diagnostic = result.Success ? null : result.Diagnostics[0].Code,
                Statistics = statistics,
            }));
        }
    }
}
