using System.Diagnostics;
using System.Text.Json;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;

/// <summary>Separates coordinate index construction from sparse and dense evaluation.</summary>
internal static class RangeIndexBenchmark
{
    /// <summary>Runs deterministic unrelated-column and existing-search workloads.</summary>
    /// <param name="count">Unrelated stored row count.</param>
    internal static void Run(int count)
    {
        foreach (var stored in new[] { 0, 1, 3 })
        {
            var coordinates = Enumerable.Range(1, count).Select(r => new FormulaCellAddress("S", r, 1))
                .Concat(new[] { 500000, 1, 1048576 }.Take(stored).Select(r => new FormulaCellAddress("S", r, 2))).ToArray();
            FormulaRangeIndex index = null!;
            Measure("index-build", count, stored, () =>
            {
                index = new FormulaRangeIndex(coordinates);
                return null;
            });
            var source = new IndexedSource(index);
            foreach (var (text, expected) in new[]
            {
                ("COUNTIF(B:B,\"\")", 1048576d - stored),
                ("SUM(B:B)", 7d * stored),
                ("SUM(A1:A" + count + ")", 7d * count),
                ("SUMIFS(A1:A" + count + ",A1:A" + count + ",\">0\")", 7d * count),
                ("MATCH(7,A:A,0)", 1d),
                ("VLOOKUP(7,A:B,1,FALSE)", 7d),
            })
            {
                var parsed = FormulaParser.Parse(text).Formula!;
                Measure(text, count, stored, () =>
                {
                    var session = new FormulaEngine().CreateSession(source, new() { MaxCellVisits = 10000000, MaxEvaluationSteps = 50000000 });
                    var result = session.Evaluate(parsed, new FormulaCellAddress("S", 1, 26));
                    if (!result.Success || result.Value.Number != expected)
                    {
                        throw new InvalidOperationException("Range benchmark mismatch: " + text + "; success=" + result.Success + "; value=" + result.Value + "; diagnostics=" + string.Join(";", result.Diagnostics.Select(d => d.Code)));
                    }

                    return session.GetStatistics();
                });
            }
        }
    }

    private static void Measure(string name, int count, int stored, Func<FormulaSessionStatistics?> operation)
    {
        for (var iteration = 0; iteration < 6; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var bytes = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var stats = operation();
            timer.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Name = name,
                Count = count,
                Stored = stored,
                Iteration = iteration,
                Warmup = iteration == 0,
                Milliseconds = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = GC.GetTotalAllocatedBytes(true) - bytes,
                Statistics = stats,
            }));
        }
    }

    private sealed class IndexedSource : ISparseFormulaDataSource
    {
        private readonly FormulaRangeIndex index;

        internal IndexedSource(FormulaRangeIndex index) => this.index = index;

        /// <inheritdoc/>
        public FormulaCellData GetCell(FormulaCellAddress address) => FormulaCellData.FromValue(FormulaValue.FromNumber(7));

        /// <inheritdoc/>
        public bool TryResolveSheet(string name, out string sheetId)
        {
            sheetId = name;
            return true;
        }

        /// <inheritdoc/>
        public bool TryResolveName(string? qualifiedSheet, string name, string currentSheetId, out FormulaRange range)
        {
            range = default;
            return false;
        }

        /// <inheritdoc/>
        public IEnumerable<FormulaCellAddress> EnumerateStoredCells(FormulaRange range, CancellationToken cancellationToken) => index.Enumerate(range, cancellationToken);
    }
}
