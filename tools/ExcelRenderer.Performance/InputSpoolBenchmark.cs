using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using ExcelRenderer.Rendering;

/// <summary>Measures input buffering independently of workbook models and native render resources.</summary>
internal static class InputSpoolBenchmark
{
    /// <summary>Creates a deterministic uncompressed ZIP fixture or measures the existing input preparer.</summary>
    /// <param name="args">Command, ZIP path, optional generate flag.</param>
    /// <returns>The asynchronous benchmark completion.</returns>
    internal static async Task RunAsync(string[] args)
    {
        if (args.Length > 2 && args[2] == "generate")
        {
            using var file = File.Create(args[1]);
            using var zip = new ZipArchive(file, ZipArchiveMode.Create);
            using var entry = zip.CreateEntry("payload.bin", CompressionLevel.NoCompression).Open();
            var random = new Random(7241);
            var buffer = new byte[65536];
            for (var block = 0; block < 512; block++)
            {
                random.NextBytes(buffer);
                entry.Write(buffer, 0, buffer.Length);
            }

            return;
        }

        using var process = Process.GetCurrentProcess();
        using var input = File.OpenRead(args[1]);
        var allocated = GC.GetTotalAllocatedBytes(false);
        var timer = Stopwatch.StartNew();
        var options = new WorkbookInputOptions
        {
            MemoryThresholdBytes = 1024 * 1024,
            AllowTemporaryFiles = true,
        };
        var prepared = await WorkbookInputPreparer.ReadAsync(input, options, default).ConfigureAwait(false);
        timer.Stop();
        process.Refresh();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            InputBytes = input.Length,
            TotalMs = timer.Elapsed.TotalMilliseconds,
            AllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
            ManagedBytes = GC.GetTotalMemory(false),
            process.PeakWorkingSet64,
            process.PrivateMemorySize64,
        }));
        ((object)prepared as IDisposable)?.Dispose();
    }
}
