using System.Text;
using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class GlyphStorageOptimizationTests
{
    [Fact]
    public void Identity_clusters_match_original_builder_for_unicode_and_invalid_input()
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        var random = new Random(91831);
        string[] elements = ["a", "日", "e\u0301", "😀", "👩‍👩‍👧", "🇯🇵", " ", "\t", "\r\n", "\u00a0", "\u2003", "\u200d", "\uD800", "\uDC00"];
        var texts = new List<string> { "", "  \t \n", "a   b", "e\u0301  😀", "a" + new string('\u0301', 2048) };
        for (var sample = 0; sample < 100; sample++)
        {
            texts.Add(string.Concat(Enumerable.Range(0, random.Next(1, 30)).Select(_ => elements[random.Next(elements.Length)])));
        }

        foreach (var text in texts)
        foreach (var delta in new[] { -1, 0, 1 })
        {
            var native = context.Face.Typeface.GetGlyphs(text);
            var glyphs = native.Take(Math.Max(0, native.Length + delta)).Concat(delta > 0 ? new ushort[] { 0 } : []).ToArray();
            AssertOriginal(context.Face, glyphs, text, new string(text.ToCharArray()), false);
        }
    }

    [Theory]
    [InlineData("a", "a\U000E0100", false)]
    [InlineData("?", "👩‍👩‍👧", false)]
    [InlineData("a", "ab", false)]
    [InlineData("ab", "a", false)]
    [InlineData("", "a", false)]
    [InlineData("ab", "", false)]
    [InlineData("a", "ab", true)]
    [InlineData("", "", true)]
    [InlineData("  ", "  ", true)]
    public void Compatibility_and_explicit_clusters_preserve_original_validation(string display, string source, bool singleCluster)
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        var glyphs = context.Face.Typeface.GetGlyphs(display);
        AssertOriginal(context.Face, glyphs, display, source, singleCluster);
    }

    [Fact]
    public void Span_storage_is_frozen_and_does_not_retain_the_paragraph_buffer()
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        var input = context.Face.Typeface.GetGlyphs("x日本😀y");
        var original = input.AsSpan(1, input.Length - 2).ToArray();
        var sequence = new GlyphSequence(context.Face, input.AsSpan(1, input.Length - 2), "日本😀", "日本😀");
        Array.Fill(input, (ushort)0);
        Assert.Equal(original, sequence.Glyphs);
        Assert.Equal(context.Face.Advances(original, 1), sequence.Advances);
        Assert.Throws<NotSupportedException>(() => ((IList<ushort>)sequence.Glyphs)[0] = 0);
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)sequence.Clusters)[0] = 99);
        Assert.Equal(Encoding.UTF8.GetBytes("日本😀"), sequence.Source);
    }

    [Fact]
    public void Core_outline_uses_one_storage_copy_per_line_and_replays_after_scope_disposal()
    {
        var text = "replay " + string.Concat(Enumerable.Repeat("日本語 e\u0301 😀    ", 300));
        var counts = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => counts[name] = counts.GetValueOrDefault(name) + value;
        TextLayoutResult layout;
        try
        {
            using (var font = new SingleFontContext(TestSupport.JapaneseFont))
            {
                layout = new SkiaTextMeasurer(font).Layout(text, new(Size: 10.125), 80, true);
            }
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }

        Assert.True(layout.Lines.Count > 10);
        Assert.Equal(layout.Lines.Count, counts["glyphStorageCopies"]);
        Assert.Equal(layout.Lines.Sum(line => line.Glyphs!.Glyphs.Count), counts["glyphStorageCopiedGlyphs"]);
        Assert.False(counts.ContainsKey("outlineIntermediateGlyphCopies"));
        Assert.Equal(layout.Lines.Count, counts["glyphClusterElementPasses"]);
        using var replayFont = new SingleFontContext(TestSupport.JapaneseFont);
        using var renderer = new CorePdfRenderer(replayFont);
        DrawCommand[] commands = [new DrawTextCommand(1, new(0, 0, 80, 800), text, CellStyle.Default with { WrapText = true, Font = new(Size: 10.125) }) { TextLayout = layout }];
        renderer.Collect(commands);
        renderer.Prepare();
        renderer.AppendPage(new(100, 800), commands);
        using var output = new MemoryStream();
        renderer.Complete(output);
        using var document = TestSupport.Open(output.ToArray());
        Assert.Equal(1, document.NumberOfPages);
        Assert.Contains("replay", document.GetPage(1).Text);
    }

    private static void AssertOriginal(SkiaFontFace face, ushort[] glyphs, string display, string source, bool singleCluster)
    {
        uint[]? expected = null;
        GlyphSequence? actual = null;
        var oldError = Record.Exception(() => expected = OriginalClusters(glyphs.Length, display, source, singleCluster));
        var newError = Record.Exception(() => actual = new(face, glyphs, display, source, singleCluster));
        Assert.Equal(oldError?.GetType(), newError?.GetType());
        Assert.Equal(oldError?.Message, newError?.Message);
        if (oldError is not null) return;
        Assert.Equal(expected, actual!.Clusters);
        Assert.Equal(glyphs, actual.Glyphs);
        Assert.Equal(Encoding.UTF8.GetBytes(source), actual.Source);
        Assert.Equal(face.Advances(glyphs, 1), actual.Advances);
    }

    // Independent snapshot of the pre-change cluster algorithm.
    private static uint[] OriginalClusters(int glyphCount, string display, string source, bool singleCluster)
    {
        var clusters = new List<uint>();
        if (singleCluster)
        {
            clusters.AddRange(new uint[glyphCount]);
        }
        else
        {
            using var originals = TextLineBreaker.Elements(source).GetEnumerator();
            var offset = 0;
            uint? whitespace = null;
            foreach (var element in TextLineBreaker.Elements(display))
            {
                if (!originals.MoveNext()) throw new InvalidDataException("Display and source Unicode clusters differ.");
                var isWhitespace = element.All(char.IsWhiteSpace);
                var cluster = isWhitespace ? whitespace ?? (uint)offset : (uint)offset;
                whitespace = isWhitespace ? cluster : null;
                for (var index = 0; index < element.Length; index++)
                {
                    clusters.Add(cluster);
                    if (char.IsHighSurrogate(element[index]) && index + 1 < element.Length && char.IsLowSurrogate(element[index + 1])) index++;
                }
                offset += Encoding.UTF8.GetByteCount(originals.Current);
            }
            if (originals.MoveNext()) throw new InvalidDataException("Unused source Unicode clusters.");
        }
        if (clusters.Count != glyphCount) throw new InvalidDataException("Glyphs and source Unicode clusters differ.");
        return clusters.ToArray();
    }
}
