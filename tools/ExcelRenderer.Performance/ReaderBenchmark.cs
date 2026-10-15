using System.Diagnostics;
using System.Text.Json;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

/// <summary>Measures reader-only allocation and phase counters with one warmup and five samples.</summary>
internal static class ReaderBenchmark
{
    /// <summary>Runs reproducible reader samples against an existing synthetic workbook.</summary>
    /// <param name="path">The input workbook path.</param>
    internal static void Run(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (var iteration = 0; iteration < 6; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var phases = new Dictionary<string, double>();
            var peakManaged = GC.GetTotalMemory(false);
            ConversionMetrics.Observer = (name, value) =>
            {
                phases[name] = phases.GetValueOrDefault(name) + value;
                peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
            };
            try
            {
                using var input = new MemoryStream(bytes, false);
                var allocated = GC.GetTotalAllocatedBytes(true);
                var timer = Stopwatch.StartNew();
                var document = new ExcelReader().Read(input);
                timer.Stop();
                using var process = Process.GetCurrentProcess();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    Iteration = iteration,
                    Warmup = iteration == 0,
                    TotalMs = timer.Elapsed.TotalMilliseconds,
                    AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                    SampledPeakManagedBytes = Math.Max(peakManaged, GC.GetTotalMemory(false)),
                    process.PeakWorkingSet64,
                    Cells = document.Sheets.Sum(sheet => sheet.Cells.Count),
                    Rows = document.Sheets.Sum(sheet => sheet.Rows.Count),
                    Columns = document.Sheets.Sum(sheet => sheet.Columns.Count),
                    Phases = phases,
                }));
            }
            finally
            {
                ConversionMetrics.Observer = null;
            }
        }
    }
}
