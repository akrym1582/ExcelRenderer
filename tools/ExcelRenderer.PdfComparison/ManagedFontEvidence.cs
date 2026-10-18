using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer.Core.Fonts.TrueType;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;

namespace ExcelRenderer.PdfComparison;

/// <summary>Exports independent-reader inputs and repeatable managed font timing evidence.</summary>
internal static class ManagedFontEvidence
{
    /// <summary>Measures three warmups and five samples and exports subset/full PDF pairs.</summary>
    /// <param name="directory">The artifact directory.</param>
    internal static void Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var root = Path.GetFullPath(".");
        var results = new List<object>();
        foreach (var (name, relative, text, explicitGlyphs) in new (string, string, string, ushort[]?)[]
        {
            ("noto", "third_party/NotoSansJP/NotoSansJP-Regular.ttf", "日本語 ABC 𠮷  ", null),
            ("ipamj", "third_party/IPAmjMincho/ipamjm.ttf", "辻葛", new ushort[] { 0, 26795, 26796, 23477 }),
            ("mono", "tests/ExcelRenderer.Tests/Fonts/NotoSansMono-Regular.ttf", "ABC abcéÅ 123", null),
        })
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, relative));
            using var data = SKData.CreateCopy(bytes);
            using var face = SKTypeface.FromData(data);
            using var original = new SKFont(face, 20);
            var glyphs = explicitGlyphs ?? original.GetGlyphs(text).Prepend((ushort)0).ToArray();
            var samples = new List<object>();
            byte[] subset = [];
            for (var iteration = -3; iteration < 5; iteration++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                subset = TrueTypeSubsetter.Create(bytes, glyphs, default);
                timer.Stop();
                if (iteration >= 0)
                {
                    samples.Add(new { ms = timer.Elapsed.TotalMilliseconds, allocated = GC.GetAllocatedBytesForCurrentThread() - allocated });
                }
            }

            using var subsetData = SKData.CreateCopy(subset);
            using var subsetFace = SKTypeface.FromData(subsetData);
            using var font = new SKFont(subsetFace, 20);
            foreach (var glyph in glyphs)
            {
                using var before = original.GetGlyphPath(glyph);
                using var after = font.GetGlyphPath(glyph);
                if (before?.ToSvgPathData() != after?.ToSvgPathData() || original.GetGlyphWidths([glyph])[0] != font.GetGlyphWidths([glyph])[0])
                {
                    throw new InvalidOperationException($"Glyph path/advance changed: {name}, GID {glyph}.");
                }
            }

            File.WriteAllBytes(Path.Combine(directory, name + "-subset.ttf"), subset);
            var beforeTables = new SfntReader(bytes);
            var afterTables = new SfntReader(subset);
            results.Add(new
            {
                kind = "subset", name, input = relative, sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                originalBytes = bytes.Length, subsetBytes = subset.Length, glyphs, samples,
                tables = beforeTables.Tables.ToDictionary(entry => entry.Key, entry => new { original = entry.Value.Length, subset = afterTables.Tables.GetValueOrDefault(entry.Key)?.Length ?? 0 }),
            });
        }

        var collectionBytes = File.ReadAllBytes(Path.Combine(root, "tests/ExcelRenderer.Tests/Fonts/PdfFormats/collection-distinct.ttc"));
        for (var index = 0; index < 2; index++)
        {
            var samples = new List<object>();
            byte[] extracted = [];
            for (var iteration = -3; iteration < 5; iteration++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                var metadata = new FontCollectionReader(collectionBytes);
                foreach (var faceIndex in Enumerable.Range(0, metadata.FaceOffsets.Length))
                {
                    metadata.ReadFace(faceIndex);
                }

                var metadataMs = timer.Elapsed.TotalMilliseconds;
                extracted = FontFaceExtractor.Extract(collectionBytes, index);
                timer.Stop();
                if (iteration >= 0)
                {
                    samples.Add(new { metadataMs, totalMs = timer.Elapsed.TotalMilliseconds, allocated = GC.GetAllocatedBytesForCurrentThread() - allocated });
                }
            }

            File.WriteAllBytes(Path.Combine(directory, $"collection-face-{index}.ttf"), extracted);
            results.Add(new { kind = "extraction", index, samples, originalBytes = collectionBytes.Length, extractedBytes = extracted.Length });
        }

        foreach (var (name, text, pages) in new[]
        {
            ("japanese", "日本語 ABC 𠮷野家", 1),
            ("ivs", "辻\U000E0102辻\U000E0103葛\U000E0102", 1),
            ("color", "日本語 辻\U000E0102 😀 ☀️", 1),
            ("collection", "A B", 1),
            ("50-pages", "日本語 50 pages ABC", 50),
            ("200-pages", "日本語 200 pages ABC", 200),
        })
        {
            var samples = new List<object>();
            for (var iteration = -3; iteration < 5; iteration++)
            {
                var metrics = new Dictionary<string, double>();
                ConversionMetrics.Observer = (key, value) => metrics[key] = metrics.GetValueOrDefault(key) + value;
                var allocated = GC.GetTotalAllocatedBytes(true);
                var timer = Stopwatch.StartNew();
                var bytes = Draw(root, name, text, pages, false);
                timer.Stop();
                using var process = Process.GetCurrentProcess();
                if (iteration >= 0)
                {
                    samples.Add(new
                    {
                        ms = timer.Elapsed.TotalMilliseconds, allocated = GC.GetTotalAllocatedBytes(true) - allocated,
                        process.PeakWorkingSet64, outputBytes = bytes.Length, metrics,
                    });
                }

                if (iteration == 4)
                {
                    File.WriteAllBytes(Path.Combine(directory, name + "-subset.pdf"), bytes);
                }

                ConversionMetrics.Observer = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            File.WriteAllBytes(Path.Combine(directory, name + "-full.pdf"), Draw(root, name, text, pages, true));
            results.Add(new { kind = "drawing", name, pages, samples });
        }

        var report = new
        {
            warmups = 3, samples = 5, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, cpu = Environment.ProcessorCount,
            peakWorkingSetScope = "process lifetime high-water mark; fresh-process comparisons use the separate benchmark script",
            results,
        };
        File.WriteAllText(Path.Combine(directory, "managed-font-evidence.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static byte[] Draw(string root, string name, string text, int pages, bool fullEmbed)
    {
        var collection = Path.Combine(root, "tests/ExcelRenderer.Tests/Fonts/PdfFormats/collection-distinct.ttc");
        var manager = new FontManager(name == "collection" ? new()
        {
            AllowSystemFonts = false, UseFontPack = false, FallbackFamilies = ["Collection"],
            Registrations = [new("Collection", collection, collection) { BoldCollectionIndex = 1 }],
        }
        : new() { AllowSystemFonts = false, IvsFontStyle = IvsFontStyle.Mincho });
        var measurer = new SkiaTextMeasurer(manager);
        var commands = new List<DrawCommand>();
        foreach (var (size, index) in new[] { 12d, 24d, 36d }.Select((size, index) => (size, index)))
        {
            var style = CellStyle.Default with { Font = new(name == "collection" ? "Collection" : "Noto Sans JP", size) };
            commands.Add(new DrawTextCommand(1, new(20, 20 + (index * 70), 550, 65), text, style)
            {
                TextLayout = measurer.Layout(text, style.Font, 550, false),
            });
            if (name == "collection")
            {
                var bold = style.Font with { Bold = true };
                commands.Add(new DrawTextCommand(1, new(220, 20 + (index * 70), 300, 65), text, style with { Font = bold })
                {
                    TextLayout = measurer.Layout(text, bold, 300, false),
                });
            }
        }

        using var writer = new SkiaPdfDocumentWriter(manager, default, fullEmbed: fullEmbed);
        writer.Collect(commands);
        writer.Prepare();
        for (var page = 0; page < pages; page++)
        {
            writer.Append(new(595.27559, 841.88976), new(0, name, page + 1, page + 1, page + 1, 595.27559, 841.88976), commands, [], _ => { });
        }

        using var output = new MemoryStream();
        writer.Complete(output);
        return output.ToArray();
    }
}
