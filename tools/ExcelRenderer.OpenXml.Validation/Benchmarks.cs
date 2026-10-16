using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer.OpenXml;

/// <summary>Isolates each reader and repetition so RSS peaks never contaminate other samples.</summary>
internal static class Benchmarks
{
    /// <summary>Runs one warmup and five independent measurements per projection and reader.</summary>
    /// <param name="input">Generated fixture directory.</param>
    /// <param name="output">JSON result path.</param>
    internal static void Run(string input, string output)
    {
        var results = new List<object>();
        var projections = new Dictionary<(string Path, string Mode), (int Count, ulong Checksum)>();
        foreach (var path in Directory.GetFiles(input, "*.xlsx").OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (var mode in new[] { "format", "parse-only" })
            {
                foreach (var reader in new[] { "openxml" })
                {
                    _ = Worker(path, reader, mode);
                    var samples = Enumerable.Range(0, 5).Select(_ => Worker(path, reader, mode)).ToArray();
                    var cells = samples.Select(s => s.GetProperty("Cells").GetInt32()).Distinct().ToArray();
                    var checksums = samples.Select(s => s.GetProperty("Checksum").GetUInt64()).Distinct().ToArray();
                    if (cells.Length != 1 || checksums.Length != 1)
                    {
                        throw new InvalidDataException("Benchmark projection is not deterministic.");
                    }

                    var key = (path, mode);
                    if (projections.TryGetValue(key, out var expected) && (expected.Count != cells[0] || expected.Checksum != checksums[0]))
                    {
                        throw new InvalidDataException("Readers produced different benchmark projections.");
                    }

                    projections[key] = (cells[0], checksums[0]);
                    double Median(string field) => samples.Select(s => s.GetProperty(field).GetDouble()).OrderBy(n => n).ElementAt(2);
                    results.Add(new
                    {
                        File = Path.GetFileName(path),
                        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                        Reader = reader,
                        Mode = mode,
                        Cells = cells[0],
                        Checksum = checksums[0],
                        ElapsedMs = Median("ElapsedMs"),
                        AllocatedBytes = Median("AllocatedBytes"),
                        PeakWorkingSetBytes = Median("PeakWorkingSetBytes"),
                        MetadataMs = Median("MetadataMs"),
                        OpenStylesMs = Median("OpenStylesMs"),
                        SharedStringsMs = Median("SharedStringsMs"),
                        EnumerationMs = Median("EnumerationMs"),
                        Gen0 = Median("Gen0"),
                        Gen1 = Median("Gen1"),
                        Gen2 = Median("Gen2"),
                        Samples = samples,
                    });
                    Console.WriteLine($"{Path.GetFileName(path)} {reader} {mode}: {Median("ElapsedMs"):F1} ms");
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(
            output,
            JsonSerializer.Serialize(
                new
                {
                    Environment = Evidence.Capture(),
                    Warmup = 1,
                    Measurements = 5,
                    Results = results,
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Measures one reader in the current fresh worker process.</summary>
    /// <param name="path">Controlled XLSX path.</param>
    /// <param name="reader">openxml. Historical engines must run from their original checkout.</param>
    /// <param name="mode">format or parse-only.</param>
    /// <returns>Timing, allocation, GC and RSS evidence.</returns>
    internal static object Measure(string path, string reader, string mode)
    {
        if (reader != "openxml")
        {
            throw new ArgumentException("Only openxml is available. Run historical benchmarks from their original checkout.", nameof(reader));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var gens = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var timer = Stopwatch.StartNew();
        var count = 0;
        ulong checksum = 14695981039346656037;
        var open = 0.0;
        var metadata = 0.0;
        var sst = 0.0;
        var enumeration = 0.0;
        var sharedItems = 0;
        var styles = 0;
        void Add(string value)
        {
            foreach (var c in value)
            {
                checksum = unchecked((checksum ^ c) * 1099511628211);
            }
        }

        using (var stream = File.OpenRead(path))
        {
            if (reader == "openxml")
            {
                var selected = Path.GetFileNameWithoutExtension(path) == "selection" ? "Small" : "Data";
                using var wb = OpenXmlWorkbookReader.Open(stream, new OpenXmlReaderOptions { SelectedSheetNames = new[] { selected }, Culture = CultureInfo.CurrentCulture });
                open = timer.Elapsed.TotalMilliseconds;
                var index = wb.Sheets.Single(s => s.Name == selected).Index;
                wb.ReadWorksheetInfo(index);
                metadata = timer.Elapsed.TotalMilliseconds - open;
                var beforeEnumeration = timer.Elapsed.TotalMilliseconds;
                foreach (var cell in wb.EnumerateCells(index))
                {
                    count++;
                    Add(cell.Address.ToString());
                    Add(mode == "format" ? wb.FormatCell(cell).Text : cell.Value.Kind);
                }

                enumeration = timer.Elapsed.TotalMilliseconds - beforeEnumeration;
                sharedItems = wb.LoadedSharedStringCount;
                styles = wb.StyleRecordCount;
                sst = wb.SharedStringLoadTime.TotalMilliseconds;
            }
        }

        timer.Stop();
        var endAllocated = GC.GetTotalAllocatedBytes(true) - allocated;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new
        {
            Cells = count,
            Checksum = checksum,
            ElapsedMs = timer.Elapsed.TotalMilliseconds,
            AllocatedBytes = endAllocated,
            PeakWorkingSetBytes = process.PeakWorkingSet64,
            Gen0 = GC.CollectionCount(0) - gens[0],
            Gen1 = GC.CollectionCount(1) - gens[1],
            Gen2 = GC.CollectionCount(2) - gens[2],
            OpenStylesMs = open,
            MetadataMs = metadata,
            SharedStringsMs = sst,
            EnumerationMs = enumeration,
            SharedItems = sharedItems,
            StyleRecords = styles,
        };
    }

    private static JsonElement Worker(string path, string reader, string mode)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "worker", "--input", path, "--reader", reader, "--mode", mode })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
        }

        return JsonDocument.Parse(stdout.GetAwaiter().GetResult()).RootElement.Clone();
    }
}
