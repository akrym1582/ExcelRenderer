using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using ExcelRenderer.Excel;

var mode = args.Length > 0 ? args[0] : "reader";
var sizes = args.Length > 1 ? args[1].Split(',').Select(int.Parse).ToArray() : new[] { 1000, 10000, 30000 };
Console.WriteLine(JsonSerializer.Serialize(new { Environment = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription, ProcessorCount = Environment.ProcessorCount, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default", Mode = mode, Warmups = 1, Repeats = 5 }));
foreach (var count in sizes)
{
    if (mode == "range-index")
    {
        RangeIndexBenchmark.Run(count);
        continue;
    }

    if (mode.StartsWith("expansion-", StringComparison.Ordinal))
    {
        ExpansionBenchmark.Run(mode, count);
        continue;
    }

    var bytes = WorkbookFixture.Create(count, mode.StartsWith("mapping", StringComparison.Ordinal));
    if (mode == "reader" || mode == "reader-recalculate")
    {
        Benchmark.Measure(mode == "reader" ? "reader-cached" : "reader-recalculate", count, () =>
        {
            using var input = new MemoryStream(bytes, false);
            var options = new ExcelReadOptions();
#if !BASELINE
            if (mode == "reader-recalculate")
            {
                options = options with { FormulaDisplayMode = FormulaDisplayMode.Recalculate };
            }
#endif
            var document = new ExcelReader().Read(input, options);
            var total = document.Sheets.Sum(sheet => sheet.Cells.Values.Count(cell => cell.Formula != null));
            if (total != count + 1)
            {
                throw new InvalidOperationException($"Expected {count + 1} formula cells, got {total}.");
            }

            var sum = document.Sheets.SelectMany(sheet => sheet.Cells.Values).Where(cell => cell.Formula != null).Sum(cell => double.Parse(cell.Text!, CultureInfo.InvariantCulture));
            if (sum != 44d * count)
            {
                throw new InvalidOperationException("Cached display mismatch: " + sum);
            }

            return sum;
        });
    }
#if !BASELINE
    else
    {
        EngineBenchmark.Run(mode, count, bytes);
    }
#endif
}
