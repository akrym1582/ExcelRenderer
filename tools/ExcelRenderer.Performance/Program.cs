using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ExcelRenderer;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

var command = args[0];
var path = args[1];
if (command == "render-profile")
{
    await RenderMemoryBenchmark.ProfileAsync(args);
    return;
}

if (command == "layout-case")
{
    TextLayoutBenchmark.Run(args);
    return;
}

if (command.StartsWith("integration-", StringComparison.Ordinal))
{
    await IntegrationBenchmark.RunAsync(args);
    return;
}

if (command == "reader-fixtures")
{
    ReaderFixtures.Generate(path);
    return;
}

if (command == "reader-case")
{
    ReaderBenchmark.Run(path);
    return;
}

if (command == "input-spool")
{
    await InputSpoolBenchmark.RunAsync(args);
    return;
}

if (command == "render-case")
{
    await RenderMemoryBenchmark.RunAsync(args);
    return;
}

if (command == "direct-images")
{
    ImageRenderingBenchmark.Run(args);
    return;
}

if (command == "generate")
{
    PerformanceFixtureGenerator.Generate(path, int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args.Length > 3 ? args[3] : "text");
    return;
}

var repeats = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1;
var fontPath = Path.GetFullPath(args.Length > 3 ? args[3] : "third_party/NotoSansJP/NotoSansJP-Regular.ttf");
var bytes = await File.ReadAllBytesAsync(path);
using var zipStream = new MemoryStream(bytes, false);
using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
var entries = zip.Entries.Select(entry =>
{
    using var source = entry.Open();
    var xml = entry.FullName.EndsWith(".xml", StringComparison.Ordinal) ? XDocument.Load(source) : null;
    return new
    {
        entry.FullName,
        entry.CompressedLength,
        entry.Length,
        Cells = xml?.Descendants().Count(element => element.Name.LocalName == "c"),
        Rows = xml?.Descendants().Count(element => element.Name.LocalName == "row"),
        Columns = xml?.Descendants().Count(element => element.Name.LocalName == "col"),
        Styled = xml?.Descendants().Count(element => element.Attribute("s") is not null),
        FormatRecords = xml?.Descendants().Count(element => element.Name.LocalName == "xf"),
        PrintRanges = xml?.Descendants().Where(element => element.Name.LocalName == "definedName").Select(element => element.Value).ToArray(),
    };
}).ToArray();
for (var iteration = 0; iteration < repeats; iteration++)
{
    var phases = new Dictionary<string, double>();
    var snapshots = new List<object>();
    ConversionMetrics.Observer = (name, value) =>
    {
        phases[name] = phases.GetValueOrDefault(name) + value;
        if (name.EndsWith(".ms", StringComparison.Ordinal))
        {
            using var snapshotProcess = Process.GetCurrentProcess();
            var snapshot = new { Phase = name, DurationMs = value, ManagedBytes = GC.GetTotalMemory(false), snapshotProcess.WorkingSet64, snapshotProcess.PrivateMemorySize64, AllocatedBytes = GC.GetTotalAllocatedBytes(false) };
            snapshots.Add(snapshot);
            Console.Error.WriteLine(JsonSerializer.Serialize(snapshot));
        }
    };
    using var input = new MemoryStream(bytes, false);
    using var output = File.Create(path + ".pdf");
    using var process = Process.GetCurrentProcess();
    var allocated = GC.GetTotalAllocatedBytes(true);
    var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
    var timer = Stopwatch.StartNew();
    var result = await ExcelConverter.RenderAsync(
        input,
        new()
        {
            OutputFormat = OutputFormat.Pdf,
            FontOptions = new()
            {
                AllowSystemFonts = false,
                UseFontPack = false,
                Registrations = [new FontRegistration("Noto Sans JP", fontPath)],
                FallbackFamilies = ["Noto Sans JP"],
            },
        },
        new SingleStreamOutputSink(output));
    timer.Stop();
    process.Refresh();
    var conversionAllocated = GC.GetTotalAllocatedBytes(true) - allocated;
    var conversionManaged = GC.GetTotalMemory(false);
    var conversionCollections = Enumerable.Range(0, 3).Select(index => GC.CollectionCount(index) - collections[index]).ToArray();
    var heap = GC.GetGCMemoryInfo();
    output.Flush();
    output.Position = 0;
    var pdfStats = ExcelRenderer.Performance.PdfInspector.Inspect(output);
    long? postCollectionManaged = null;
    long? postCollectionWorkingSet = null;
    long? postCollectionPrivateBytes = null;
    if (repeats > 1)
    {
        // Harness-only retention check; collection is outside conversion timing.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        postCollectionManaged = GC.GetTotalMemory(false);
        process.Refresh();
        postCollectionWorkingSet = process.WorkingSet64;
        postCollectionPrivateBytes = process.PrivateMemorySize64;
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Iteration = iteration,
        OS = RuntimeInformation.OSDescription,
        Runtime = RuntimeInformation.FrameworkDescription,
        CPU = Environment.ProcessorCount,
        InputSHA256 = Convert.ToHexString(SHA256.HashData(bytes)),
        FontSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath))),
        AllowSystemFonts = false,
        InputBytes = bytes.Length,
        TotalMs = timer.Elapsed.TotalMilliseconds,
        AllocatedBytes = conversionAllocated,
        process.PeakWorkingSet64,
        process.WorkingSet64,
        process.PrivateMemorySize64,
        ManagedBytes = conversionManaged,
        PostCollectionManagedBytes = postCollectionManaged,
        PostCollectionWorkingSet64 = postCollectionWorkingSet,
        PostCollectionPrivateBytes = postCollectionPrivateBytes,
        GC = conversionCollections,
        Heap = heap.GenerationInfo.ToArray().Select(info => new { info.SizeAfterBytes, info.FragmentationAfterBytes }),
        FontObjects = pdfStats.FontObjects,
        ImageObjects = pdfStats.ImageObjects,
        FontStreams = pdfStats.FontStreams,
        FontStreamBytes = pdfStats.FontStreamBytes,
        Pages = result.Pages.Count,
        OutputBytes = output.Length,
        Phases = phases,
        Snapshots = snapshots,
        Zip = entries,
    }));
    ConversionMetrics.Observer = null;
}
