using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

/// <summary>Separates diagnostic observations from uninstrumented render/read measurements.</summary>
internal static partial class RenderMemoryBenchmark
{
    /// <summary>Runs one saved case with a diagnostic capture, warmups and measured samples.</summary>
    /// <param name="args">Command, JSON specification, case name, warmup count and sample count.</param>
    /// <returns>The asynchronous completion.</returns>
    internal static async Task ProfileAsync(string[] args)
    {
        var specification = JsonSerializer.Deserialize<ProfileCase[]>(File.ReadAllText(args[1]))!;
        var item = specification.Single(value => value.Name == args[2]);
        var warmups = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
        var samples = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        var inputBytes = File.ReadAllBytes(item.Input);
        var templateHash = Hash(inputBytes);
        if (item.Mode.Contains("mapped", StringComparison.Ordinal))
        {
            using var template = new MemoryStream(inputBytes, false);
            using var mapped = new MemoryStream();
            ExcelRenderer.Mapping.ExcelTemplateMapper.Map(template, mapped, new { items = Enumerable.Range(0, 10000).Select(index => index % 101).ToArray() });
            inputBytes = mapped.ToArray();
        }

        var snapshots = typeof(ExcelRenderer.OpenXml.Reader.OpenXmlWorkbookReader).Assembly.GetType("ExcelRenderer.OpenXml.Reader.Diagnostics.OperationMetrics")!.GetProperty("Observer", BindingFlags.Static | BindingFlags.NonPublic)!;
        var diagnosticOnly = args.Length > 5 && args[5] == "diagnostic";
        var font = Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf");
        var fontOptions = new FontOptions { AllowSystemFonts = false, UseFontPack = item.FontPack, FontFiles = [font] };
        ExcelRenderer.Pdf.PdfRenderObserver.Timestamp = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Local);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            environment = new
            {
                runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(),
                item,
                inputSha256 = Hash(inputBytes),
                templateSha256 = templateHash,
                fontSha256 = Hash(File.ReadAllBytes(font)),
                dlls = Directory.GetFiles(AppContext.BaseDirectory, "ExcelRenderer*.dll").ToDictionary(file => Path.GetFileName(file)!, file => Hash(File.ReadAllBytes(file))),
            },
        }));
        for (var iteration = diagnosticOnly ? 0 : -warmups - 1; iteration < (diagnosticOnly ? 1 : samples); iteration++)
        {
            var diagnostic = diagnosticOnly;
            var metrics = new Dictionary<string, double>();
            ConversionMetrics.Observer = null;
            if (diagnostic)
            {
                ConversionMetrics.Observer = (name, value) =>
                {
                    metrics[name] = metrics.GetValueOrDefault(name) + value;
                    if (name.EndsWith(".ms", StringComparison.Ordinal))
                    {
                        metrics[name + ".count"] = metrics.GetValueOrDefault(name + ".count") + 1;
                    }
                };
            }

            snapshots.SetValue(null, diagnostic ? (Action<string, long>)((name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value) : null);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            var allocated = GC.GetTotalAllocatedBytes(true);
            var clock = Stopwatch.StartNew();
            long outputBytes;
            int pages;
            using (var input = new MemoryStream(inputBytes, false))
            {
                if (item.ReadOnly)
                {
                    var document = new ExcelReader(new FontManager(fontOptions)).Read(input, new()
                    {
                        ApplyConditionalFormatting = item.ConditionalFormatting,
                        FormulaDisplayMode = item.Mode.StartsWith("recalculate", StringComparison.Ordinal) ? FormulaDisplayMode.Recalculate : FormulaDisplayMode.CachedValue,
                        GetSelectedRange = item.Mode.Contains("selected", StringComparison.Ordinal) ? _ => (1, 1, 5, 1) : null,
                    });
                    outputBytes = document.Sheets.Sum(sheet => (long)sheet.Cells.Count);
                    pages = document.Sheets.Count;
                }
                else
                {
                    var request = new RenderRequest
                    {
                        OutputFormat = Enum.Parse<OutputFormat>(item.Format),
                        ImageLayout = item.Continuous ? ImageLayoutMode.Continuous : ImageLayoutMode.Paginated,
                        FontOptions = fontOptions,
                        ReadOptions = new() { ApplyConditionalFormatting = item.ConditionalFormatting },
                        Trim = new() { Enabled = item.Trim },
                        Selection = new() { Pages = item.Pages, Ranges = item.Range is { } range ? [new("Synthetic", CellRangeParser.Parse(range, out _))] : null },
                        Dpi = 72,
                    };
                    var sink = diagnostic ? (IRenderOutputSink)new ProfileSink(item.Capture) : new DiscardSink();
                    var result = await ExcelConverter.RenderAsync(input, request, sink).ConfigureAwait(false);
                    outputBytes = result.Artifacts.Sum(artifact => artifact.ByteLength);
                    pages = result.Pages.Count;
                }
            }

            clock.Stop();
            var allocationBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            using var process = Process.GetCurrentProcess();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                name = item.Name, iteration, diagnostic, cold = !diagnostic && iteration == -warmups - 1, warmup = iteration < 0,
                milliseconds = clock.Elapsed.TotalMilliseconds, allocationBytes,
                gc = collections.Select((count, generation) => GC.CollectionCount(generation) - count).ToArray(),
                peakRssBytes = process.PeakWorkingSet64, outputBytes, pages, metrics,
            }));
            ConversionMetrics.Observer = null;
            snapshots.SetValue(null, null);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record ProfileCase(string Name, string Input, string Format = "Pdf", bool ReadOnly = false, bool ConditionalFormatting = true, bool Continuous = false, bool Trim = false, int[]? Pages = null, string? Range = null, string? Capture = null, string Mode = "cached", bool FontPack = false);

    private sealed class ProfileSink(string? directory) : IRenderOutputSink
    {
        private readonly Dictionary<string, Stream> streams = [];

        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
        {
            if (directory is null)
            {
                return new(Stream.Null);
            }

            Directory.CreateDirectory(directory);
            var stream = File.Create(Path.Combine(directory, artifact.RelativeName));
            streams.Add(artifact.ArtifactId, stream);
            return new(stream);
        }

        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
        {
            if (streams.Remove(artifact.ArtifactId, out var stream))
            {
                stream.Dispose();
            }

            return default;
        }

        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => CompleteAsync(artifact, 0, cancellationToken);
    }
}
