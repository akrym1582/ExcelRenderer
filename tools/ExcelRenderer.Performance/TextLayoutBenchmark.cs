using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;

/// <summary>Measures unique long text separately from workbook input, drawing and PDF saving.</summary>
internal static class TextLayoutBenchmark
{
    /// <summary>Runs three warm-ups and one measured batch with conversion-owned font resources.</summary>
    /// <param name="args">Command, workload and optional batch size.</param>
    internal static void Run(string[] args)
    {
        var workload = args[1];
        if (workload is not ("plain" or "rich" or "rich-wrap"))
        {
            throw new ArgumentException("Workload must be plain, rich or rich-wrap.", nameof(args));
        }

        var count = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 30;
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(args));
        }

        var fontPath = Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf");
        var font = new FontStyle("Noto Sans JP", 10.125);
        var alternate = font with { Size = 12, Bold = true, Color = new(160, 0, 0) };
        var texts = Enumerable.Range(0, count).Select(index => index.ToString(CultureInfo.InvariantCulture) + " " +
            string.Concat(Enumerable.Repeat("ABC 日本語 e\u0301 0123 ", workload == "rich-wrap" ? 12 : 200))).ToArray();
        var inputs = texts.Select(text => new CellTextRun[] { new(text, font), new(text, alternate) }).ToArray();
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var manager = new FontManager(new()
            {
                AllowSystemFonts = false,
                UseFontPack = false,
                Registrations = [new("Noto Sans JP", fontPath)],
                FallbackFamilies = ["Noto Sans JP"],
            });
            using var resources = new ConversionFontResources();
            var measurer = new SkiaTextMeasurer(manager, cacheLayouts: true);
            var results = new List<TextLayoutResult>(count);
            var allocated = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            for (var index = 0; index < count; index++)
            {
                results.Add(workload == "plain"
                    ? measurer.Layout(texts[index], font, 100000, false)
                    : measurer.Layout(inputs[index], font, workload == "rich-wrap" ? 80 : 100000, workload == "rich-wrap"));
            }

            timer.Stop();
            var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var result in results)
            {
                // Exact text, style, dimensions and run positions are inspected outside timing.
                var projection = new
                {
                    result.Size,
                    Lines = result.Lines.Select(line => new
                    {
                        line.Text,
                        line.Width,
                        line.Height,
                        line.Baseline,
                        Runs = line.Runs.Select(run => new { run.Run.Text, run.Run.SourceText, run.X, run.Advance, run.Font }),
                    }),
                };
                digest.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(projection)));
            }

            using var process = Process.GetCurrentProcess();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Workload = workload,
                Warmup = iteration < 3,
                Iteration = iteration,
                Count = count,
                TotalMs = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = allocatedBytes,
                process.PeakWorkingSet64,
                Lines = results.Sum(result => result.Lines.Count),
                OutputSha256 = Convert.ToHexString(digest.GetHashAndReset()),
                InputSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(texts)))),
                FontSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath))),
            }));
        }
    }
}
