using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Writes representative rendering metrics per page and aggregates rendered pages per sheet.</summary>
internal sealed class PageMetricLog
{
    private static readonly (string Name, string Label)[] Fields =
    [
        ("commands", "commands"), ("pageBuild.ms", "buildMs"), ("textFinalize.ms", "textMs"),
        ("textGlyphCalls", "glyphCalls"), ("textGlyphCharacters", "glyphChars"),
        ("imageDecode", "imageDecodes"), ("imageCacheHit", "imageCacheHits"),
        ("pngEncode.ms", "encodeMs"), ("pngBitmapBytes", "bitmapPeakBytes"),
        ("imageCacheEstimatedBytes", "imageCachePeakBytes"), ("pagePayloadMax", "payloadPeak"),
    ];

    private static readonly JsonSerializerOptions NameOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly HashSet<string> ObservedNames = new(Fields.Select(field => field.Name).Append("pagePayloadActive"), StringComparer.Ordinal);
    private readonly TextWriter output;
    private readonly Dictionary<string, SheetMetrics> sheets = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>Initializes a new instance of the <see cref="PageMetricLog"/> class.</summary>
    /// <param name="output">The standard-output writer.</param>
    internal PageMetricLog(TextWriter output) => this.output = output;

    /// <summary>Starts a rendering-only scope for one page.</summary>
    /// <param name="sheetName">The source worksheet name.</param>
    /// <param name="sourcePage">The page number within the source sheet.</param>
    /// <param name="outputPage">The selected output page number, or null for a continuous canvas.</param>
    /// <returns>A scope that prints a page summary when rendering ends.</returns>
    internal IDisposable BeginPage(string sheetName, int sourcePage, int? outputPage)
    {
        lock (gate)
        {
            if (!sheets.TryGetValue(sheetName, out var sheet))
            {
                sheet = new();
                sheets.Add(sheetName, sheet);
            }

            return new PageScope(this, sheet, sheetName, sourcePage, outputPage);
        }
    }

    /// <summary>Writes one summary per sheet in rendering order.</summary>
    internal void WriteSheetSummaries()
    {
        lock (gate)
        {
            foreach (var (name, sheet) in sheets)
            {
                output.WriteLine($"Sheet {JsonSerializer.Serialize(name, NameOptions)}: pages={sheet.Pages} {Describe(sheet.Metrics, sheet.ElapsedMs, sheet.AllocatedBytes)}");
            }
        }
    }

    private static string Describe(MetricSummary metrics, double elapsedMs, long allocatedBytes)
    {
        var text = new StringBuilder(FormattableString.Invariant($"renderMs={elapsedMs:F3} managedAllocatedBytes={allocatedBytes}"));
        var values = metrics.Snapshot().ToDictionary();
        foreach (var (name, label) in Fields)
        {
            if (values.TryGetValue(name, out var value))
            {
                text.Append(' ').Append(label).Append('=').Append(value.ToString(name.EndsWith(".ms", StringComparison.Ordinal) ? "F3" : "F0", CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    private sealed class SheetMetrics
    {
        internal MetricSummary Metrics { get; } = new();

        internal int Pages { get; set; }

        internal double ElapsedMs { get; set; }

        internal long AllocatedBytes { get; set; }
    }

    private sealed class PageScope : IDisposable
    {
        private readonly PageMetricLog owner;
        private readonly SheetMetrics sheet;
        private readonly string sheetName;
        private readonly int sourcePage;
        private readonly int? outputPage;
        private readonly Action<string, double>? previous;
        private readonly MetricSummary metrics = new();
        private readonly long allocatedBefore;
        private readonly Stopwatch timer;
        private bool disposed;

        internal PageScope(PageMetricLog owner, SheetMetrics sheet, string sheetName, int sourcePage, int? outputPage)
        {
            this.owner = owner;
            this.sheet = sheet;
            this.sheetName = sheetName;
            this.sourcePage = sourcePage;
            this.outputPage = outputPage;
            allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            timer = Stopwatch.StartNew();
            previous = Core.Rendering.ConversionMetrics.Observer;
            Core.Rendering.ConversionMetrics.Observer = (name, value) =>
            {
                if (ObservedNames.Contains(name))
                {
                    metrics.Report(name, value);
                    sheet.Metrics.Report(name, value);
                }

                previous?.Invoke(name, value);
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            timer.Stop();
            Core.Rendering.ConversionMetrics.Observer = previous;
            lock (owner.gate)
            {
                sheet.Pages++;
                sheet.ElapsedMs += timer.Elapsed.TotalMilliseconds;
                sheet.AllocatedBytes += allocated;
                owner.output.WriteLine($"Page {JsonSerializer.Serialize(sheetName, NameOptions)} source={sourcePage} output={outputPage?.ToString(CultureInfo.InvariantCulture) ?? "continuous"}: {Describe(metrics, timer.Elapsed.TotalMilliseconds, allocated)}");
            }
        }
    }
}
