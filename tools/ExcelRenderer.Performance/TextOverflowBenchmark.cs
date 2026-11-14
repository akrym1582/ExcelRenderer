using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

/// <summary>Records conversion samples and visual artifacts for the horizontal text overflow contract.</summary>
internal static class TextOverflowBenchmark
{
    /// <summary>Runs immutable fixture conversions, with two excluded warm-ups and per-round allocation samples.</summary>
    /// <param name="args">Command, fixture, engine (main/core), and measured round count.</param>
    /// <returns>The asynchronous conversion work.</returns>
    internal static async Task RunAsync(string[] args)
    {
        var bytes = File.ReadAllBytes(args[1]);
        var rounds = int.Parse(args[3], CultureInfo.InvariantCulture);
        var font = Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf");
        var engine = args[2];
        var request = new RenderRequest
        {
            OutputFormat = OutputFormat.Pdf,
            FontOptions = new()
            {
                AllowSystemFonts = false,
                UseFontPack = false,
                Registrations = [new FontRegistration("Noto Sans JP", font)],
                FallbackFamilies = ["Noto Sans JP"],
            },
        };
        for (var round = -2; round < rounds; round++)
        {
            using var input = new MemoryStream(bytes, false);
            using var output = new MemoryStream();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var pages = engine == "core"
                ? (await ExcelRenderer.Core.ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = font })).PageCount
                : (await ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output))).Pages.Count;
            timer.Stop();
            var allocation = GC.GetTotalAllocatedBytes(true) - allocated;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Engine = engine,
                Round = round,
                Warmup = round < 0,
                Milliseconds = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = allocation,
                Pages = pages,
                InputSHA256 = Hash(bytes),
                FontSHA256 = Hash(File.ReadAllBytes(font)),
                MainSHA256 = Hash(File.ReadAllBytes(typeof(ExcelConverter).Assembly.Location)),
                CoreSHA256 = Hash(File.ReadAllBytes(typeof(ExcelRenderer.Core.ExcelConverter).Assembly.Location)),
            }));
        }
    }

    /// <summary>Records the existing reader fixtures under the legacy column policy for reviewed visual changes.</summary>
    /// <param name="directory">The local output directory.</param>
    /// <returns>The asynchronous conversion work.</returns>
    internal static async Task SnapshotsAsync(string directory)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var fixtures = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText("tests/ExcelRenderer.Tests/ReaderGolden/artifacts.json"))!;
        var hashes = new Dictionary<string, Dictionary<string, string>>();
        foreach (var path in fixtures.Keys)
        {
            var artifacts = new Dictionary<string, string>();
            hashes.Add(path, artifacts);
            foreach (var format in new[] { OutputFormat.Png, OutputFormat.Svg })
            {
                using var input = File.OpenRead(path);
                using var sink = new SnapshotSink();
                await ExcelConverter.RenderAsync(
                    input,
                    new()
                {
                    OutputFormat = format,
                    ReadOptions = new() { ColumnWidthMetrics = new() { Mode = ColumnWidthMetricsMode.RenderingFontLegacy } },
                    FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf")] },
                },
                    sink);
                foreach (var (name, stream) in sink.Artifacts)
                {
                    var bytes = stream.ToArray();
                    artifacts.Add(format + "/" + name, Hash(format == OutputFormat.Svg ? Encoding.UTF8.GetBytes(CanonicalSvg(bytes)) : bytes));
                    var destination = Path.Combine(directory, path, format.ToString(), name);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, bytes);
                }
            }
        }

        File.WriteAllText(Path.Combine(directory, "hashes.json"), JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string CanonicalSvg(byte[] bytes)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        var ids = document.Root!.DescendantsAndSelf().Attributes("id").Select((attribute, index) => (attribute.Value, Name: "resource" + index))
            .ToDictionary(item => item.Value, item => item.Name, StringComparer.Ordinal);
        foreach (var attribute in document.Root.DescendantsAndSelf().Attributes())
        {
            if (attribute.Name.LocalName == "id")
            {
                attribute.Value = ids[attribute.Value];
            }
            else if (attribute.Value.StartsWith("url(#", StringComparison.Ordinal) && attribute.Value.EndsWith(")", StringComparison.Ordinal) && ids.TryGetValue(attribute.Value.Substring(5, attribute.Value.Length - 6), out var referenced))
            {
                attribute.Value = "url(#" + referenced + ")";
            }
            else if (attribute.Value.StartsWith("#", StringComparison.Ordinal) && ids.TryGetValue(attribute.Value.Substring(1), out referenced))
            {
                attribute.Value = "#" + referenced;
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private sealed class SnapshotSink : IRenderOutputSink, IDisposable
    {
        /// <summary>Gets captured output streams by their relative artifact name.</summary>
        internal Dictionary<string, MemoryStream> Artifacts { get; } = [];

        /// <inheritdoc/>
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
        {
            var stream = new MemoryStream();
            Artifacts.Add(artifact.RelativeName, stream);
            return new(stream);
        }

        /// <inheritdoc/>
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;

        /// <inheritdoc/>
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var stream in Artifacts.Values)
            {
                stream.Dispose();
            }
        }
    }
}
