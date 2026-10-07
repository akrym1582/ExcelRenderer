using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

/// <summary>Runs the same synthetic workbook through all formats and both image layouts.</summary>
internal static class RenderMemoryBenchmark
{
    /// <summary>Runs a reproducible fresh-process benchmark or repeated conversion retention check.</summary>
    /// <param name="args">Command, fixture path, format, image layout, repetitions and optional page selection.</param>
    /// <returns>The asynchronous benchmark completion.</returns>
    internal static async Task RunAsync(string[] args)
    {
        var format = Enum.Parse<OutputFormat>(args[2], ignoreCase: true);
        var layout = Enum.Parse<ImageLayoutMode>(args[3], ignoreCase: true);
        var repeats = args.Length > 4 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 1;
        var selectedPage = args.Length > 5 && int.TryParse(args[5], out var number) ? (int?)number : null;
        var fontPack = args.Contains("--font-pack", StringComparer.Ordinal);
        var systemFonts = args.Contains("--system-fonts", StringComparer.Ordinal);
        var fontPath = Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf");
        for (var iteration = 0; iteration < repeats; iteration++)
        {
            var metrics = new Dictionary<string, double>();
            var active = 0d;
            var snapshots = new List<object>();
            var sampled = new HashSet<string>();
            long peakManaged = 0, peakPrivate = 0, peakResidual = 0;
            var sampleLock = new object();
            using var process = Process.GetCurrentProcess();
            ConversionMetrics.Observer = (name, value) =>
            {
                if (name == "pagePayloadActive")
                {
                    active += value;
                    metrics["pagePayloadMax"] = Math.Max(metrics.GetValueOrDefault("pagePayloadMax"), active);
                }
                else if (name is "imageCacheEstimatedBytes" or "pngBitmapBytes" or "pagePayloadMax" or "svgBufferMemoryPeakBytes")
                {
                    metrics[name] = Math.Max(metrics.GetValueOrDefault(name), value);
                }
                else
                {
                    metrics[name] = metrics.GetValueOrDefault(name) + value;
                }

                if (name.EndsWith(".ms", StringComparison.Ordinal) && sampled.Add(name))
                {
                    process.Refresh();
                    snapshots.Add(new { Phase = name, ManagedBytes = GC.GetTotalMemory(false), process.WorkingSet64, process.PrivateMemorySize64 });
                }
            };
            using var input = File.OpenRead(args[1]);
            using var sampler = new Timer(
                _ =>
                {
                    using var sampledProcess = Process.GetCurrentProcess();
                    var managed = GC.GetTotalMemory(false);
                    var privateBytes = sampledProcess.PrivateMemorySize64;
                    var residual = Math.Max(0, sampledProcess.WorkingSet64 - managed);
                    lock (sampleLock)
                    {
                        peakManaged = Math.Max(peakManaged, managed);
                        peakPrivate = Math.Max(peakPrivate, privateBytes);
                        peakResidual = Math.Max(peakResidual, residual);
                    }
                },
                null,
                0,
                50);
            var allocated = GC.GetTotalAllocatedBytes(false);
            var timer = Stopwatch.StartNew();
            var request = new RenderRequest
            {
                OutputFormat = format,
                ImageLayout = layout,
                Dpi = 24,
                Selection = selectedPage is { } page ? new SelectionOptions { Pages = [page] } : new(),
                FontOptions = new()
                {
                    AllowSystemFonts = systemFonts,
                    UseFontPack = fontPack,
                    Registrations = [new FontRegistration("Noto Sans JP", fontPath)],
                    FallbackFamilies = ["Noto Sans JP"],
                },
            };
            var result = await ExcelConverter.RenderAsync(input, request, new DiscardSink()).ConfigureAwait(false);
            timer.Stop();
            await sampler.DisposeAsync().ConfigureAwait(false);
            process.Refresh();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Format = format.ToString(),
                Layout = layout.ToString(),
                Iteration = iteration,
                OS = RuntimeInformation.OSDescription,
                Runtime = RuntimeInformation.FrameworkDescription,
                CPU = Environment.ProcessorCount,
                AllowSystemFonts = systemFonts,
                UseFontPack = fontPack,
                TotalMs = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
                ManagedBytes = GC.GetTotalMemory(false),
                PeakManagedSampleBytes = peakManaged,
                PeakPrivateSampleBytes = peakPrivate,
                PeakRssMinusManagedSampleBytes = peakResidual,
                process.PeakWorkingSet64,
                process.WorkingSet64,
                process.PrivateMemorySize64,
                Pages = result.Pages.Count,
                OutputBytes = result.Artifacts.Sum(artifact => artifact.ByteLength),
                MaxPngPixelBufferBytes = format == OutputFormat.Png ? result.Pages.Max(page => (long)(page.PixelWidth ?? 0) * (page.PixelHeight ?? 0) * 4) : 0,
                Metrics = metrics,
                Snapshots = snapshots,
            }));
            ConversionMetrics.Observer = null;
            if (repeats > 1)
            {
                // Harness-only collection, outside conversion timing, distinguishes live retention from GC scheduling.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                process.Refresh();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    PostCollectionIteration = iteration,
                    ManagedBytes = GC.GetTotalMemory(false),
                    process.WorkingSet64,
                    process.PrivateMemorySize64,
                }));
            }
        }
    }

    private sealed class DiscardSink : IRenderOutputSink
    {
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(Stream.Null);

        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;

        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
    }
}
