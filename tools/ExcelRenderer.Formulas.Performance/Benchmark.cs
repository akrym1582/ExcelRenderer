using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using ExcelRenderer.Excel;

/// <summary>Runs measured synchronous operations with results checked inside each sample.</summary>
internal static class Benchmark
{
    /// <summary>Writes one warmup and five measured samples as JSON lines.</summary>
    /// <param name="name">Case name.</param>
    /// <param name="count">Workload count.</param>
    /// <param name="action">Operation including correctness validation.</param>
    internal static void Measure(string name, int count, Func<double> action)
    {
        for (var iteration = 0; iteration < 6; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var result = action();
            timer.Stop();
            using var process = Process.GetCurrentProcess();
            Console.WriteLine(JsonSerializer.Serialize(new { Name = name, Count = count, Iteration = iteration, Warmup = iteration == 0, Milliseconds = timer.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, ProcessPeakWorkingSetBytes = process.PeakWorkingSet64, Result = result }));
        }
    }
}
