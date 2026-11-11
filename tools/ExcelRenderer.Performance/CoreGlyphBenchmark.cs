using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;

/// <summary>Measures Core glyph storage without workbook input, font loading or cache hits.</summary>
internal static class CoreGlyphBenchmark
{
    /// <summary>Runs three warm-ups and three measured batches against fixed glyph input.</summary>
    /// <param name="args">Command, workload, count and optional wrap width.</param>
    internal static void Run(string[] args)
    {
        var workload = args[1];
        var count = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20;
        var width = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 80;
        if (workload is not ("clusters" or "compatibility" or "outline"))
        {
            throw new ArgumentException("Unknown glyph workload.", nameof(args));
        }

        var fontPath = Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf");
        using var font = new SingleFontContext(fontPath);
        var texts = Enumerable.Range(0, count).Select(index => index.ToString(CultureInfo.InvariantCulture) + " " +
            string.Concat(Enumerable.Repeat("日本 e\u0301 😀 👩‍👩‍👧 \t  ", 200))).ToArray();
        var sources = workload == "compatibility" ? texts.Select(text => text.Replace("日", "日\U000E0100", StringComparison.Ordinal)).ToArray() : texts;
        var inputs = texts.Select(text => font.Face.Typeface.GetGlyphs(text)).ToArray();
        for (var iteration = 0; iteration < 6; iteration++)
        {
            var sequences = new List<GlyphSequence>();
            var layouts = new List<ExcelRenderer.Core.Abstractions.TextLayoutResult>();
            var measurer = new SkiaTextMeasurer(font);
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var clock = Stopwatch.StartNew();
            for (var index = 0; index < count; index++)
            {
                if (workload == "outline")
                {
                    layouts.Add(measurer.Layout(texts[index], new FontStyle(Size: 10.125), width, true));
                }
                else
                {
                    sequences.Add(new(font.Face, inputs[index], texts[index], sources[index]));
                }
            }

            clock.Stop();
            var allocationBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            var gc = collections.Select((value, generation) => GC.CollectionCount(generation) - value).ToArray();
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var layout in layouts)
            {
                digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    layout.Size,
                    layout.EffectiveFontSize,
                    Lines = layout.Lines.Select(line => new { line.Text, line.Width, line.Height, line.Baseline, line.Ascent, line.Descent, line.Leading, line.ExplicitBreak }),
                }));
                sequences.AddRange(layout.Lines.Select(line => line.Glyphs!));
            }

            foreach (var sequence in sequences)
            {
                digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { sequence.FontIdentity, sequence.Glyphs, sequence.Source, sequence.Clusters, sequence.Advances }));
            }

            using var process = Process.GetCurrentProcess();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                workload, count, width, iteration, cold = iteration == 0, warmup = iteration < 3,
                milliseconds = clock.Elapsed.TotalMilliseconds, allocationBytes, gc, peakRssBytes = process.PeakWorkingSet64,
                inputSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(texts) + string.Concat(sources)))),
                fontSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath))),
                outputSha256 = Convert.ToHexString(digest.GetHashAndReset()),
            }));
        }
    }
}
