using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;

/// <summary>Measures direct PNG and SVG rendering separately from workbook parsing and layout.</summary>
internal static class ImageRenderingBenchmark
{
    /// <summary>Renders a deterministic text or rectangle workload and reports time, allocations and output hashes.</summary>
    /// <param name="args">Command, output directory, format, workload and optional repeat count.</param>
    internal static void Run(string[] args)
    {
        var directory = Path.GetFullPath(args[1]);
        var format = args[2];
        var workload = args[3];
        var text = workload == "text";
        var repeats = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 3;
        if (format is not ("png" or "svg"))
        {
            throw new ArgumentException("Format must be png or svg.", nameof(args));
        }

        if (workload is not ("text" or "shapes" or "large"))
        {
            throw new ArgumentException("Workload must be text, shapes or large.", nameof(args));
        }

        Directory.CreateDirectory(directory);
        var manager = new FontManager(new()
        {
            AllowSystemFonts = false,
            UseFontPack = false,
            Registrations = [new("Noto Sans JP", Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf"))],
            FallbackFamilies = ["Noto Sans JP"],
        });
        using (var resources = new ConversionFontResources())
        {
            // Warm resolution outside measurement; direct rendering still has to acquire its own native faces.
            manager.ResolveTextRuns("帳票 ABC 123", new("Noto Sans JP"));
        }

        var commands = new List<DrawCommand>();
        for (var page = 1; page <= 3; page++)
        {
            for (var index = 0; index < (text ? 20 : workload == "large" ? 1 : 10000); index++)
            {
                var bounds = new ReportRect((index % 100) * 12, (index / 100) * 12, text ? 200 : 10, text ? 30 : 10);
                commands.Add(text
                    ? new DrawTextCommand(page, bounds, "帳票 ABC 123", CellStyle.Default with { Font = new("Noto Sans JP", 10) })
                    : new FillRectangleCommand(page, bounds, new ReportColor((byte)(index % 256), 80, 160)));
            }
        }

        for (var iteration = 0; iteration < repeats; iteration++)
        {
            var metrics = new Dictionary<string, double>();
            var paths = new List<string>();
            ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
            var allocated = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            try
            {
                Stream Output(int page)
                {
                    var path = Path.Combine(directory, $"{iteration}-{page}.{format}");
                    paths.Add(path);
                    return File.Create(path);
                }

                if (format == "png")
                {
                    new PngRenderer(manager).Render(commands, new PageSettings(workload == "large" ? 4096 : 1200, workload == "large" ? 4096 : 1200), Output, 72);
                }
                else
                {
                    new SvgRenderer(manager).Render(commands, new PageSettings(1200, 1200), Output);
                }
            }
            finally
            {
                timer.Stop();
                ConversionMetrics.Observer = null;
            }

            var renderingAllocated = GC.GetTotalAllocatedBytes(true) - allocated;
            using var process = Process.GetCurrentProcess();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Iteration = iteration,
                Format = format,
                Workload = args[3],
                TotalMs = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = renderingAllocated,
                process.PeakWorkingSet64,
                Metrics = metrics,
                Outputs = paths.Select(path => new
                {
                    Bytes = new FileInfo(path).Length,
                    SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                }).ToArray(),
            }));
        }
    }
}
