using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using SkiaSharp;

/// <summary>Isolates shared fill/stroke color requests from workbook layout and actual drawing.</summary>
internal static class PaintColorBenchmark
{
    /// <summary>Runs three warmups and three measured batches without an observer.</summary>
    /// <param name="args">Command, workload and request count.</param>
    internal static void Run(string[] args)
    {
        var workload = args[1];
        if (workload is not ("fill-repeat" or "fill-alternate" or "stroke-repeat" or "stroke-alternate"))
        {
            throw new ArgumentException("Unknown Paint workload.", nameof(args));
        }

        var count = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 300000;
        var stroke = workload.StartsWith("stroke", StringComparison.Ordinal);
        var alternate = workload.EndsWith("alternate", StringComparison.Ordinal);
        ReportColor[] colors = [new(20, 30, 40, 100), new(20, 30, 40, 101)];
        BorderSide[] sides = [new(1.25, colors[0]), new(1.25, colors[1])];
        var inputSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { workload, count, colors, sides })));
        for (var iteration = 0; iteration < 6; iteration++)
        {
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var clock = Stopwatch.StartNew();
            SKColor finalColor;
            float finalWidth;
            using (var resources = new SkiaPaintResources())
            {
                SKPaint? paint = null;
                for (var index = 0; index < count; index++)
                {
                    var selected = alternate ? index & 1 : 0;
                    paint = stroke ? resources.Stroke(sides[selected]) : resources.Fill(colors[selected]);
                }

                clock.Stop();
                var allocationBytes = GC.GetTotalAllocatedBytes(true) - allocated;
                var gc = collections.Select((value, generation) => GC.CollectionCount(generation) - value).ToArray();
                finalColor = paint!.Color;
                finalWidth = paint.StrokeWidth;
                using var process = Process.GetCurrentProcess();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    workload, count, iteration, cold = iteration == 0, warmup = iteration < 3,
                    milliseconds = clock.Elapsed.TotalMilliseconds, allocationBytes, gc, peakRssBytes = process.PeakWorkingSet64,
                    inputSha256,
                    outputSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { finalColor.Red, finalColor.Green, finalColor.Blue, finalColor.Alpha, finalWidth }))),
                }));
            }
        }
    }
}
