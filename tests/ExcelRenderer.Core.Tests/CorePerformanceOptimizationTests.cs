using System.Text;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class CorePerformanceOptimizationTests
{
    [Fact]
    public void Outline_wrap_matches_original_full_candidate_measurement_and_clusters()
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        var measurer = new SkiaTextMeasurer(context);
        var random = new Random(43119);
        string[] elements = ["a", " ", "日", "本", "e\u0301", "😀", "👩‍👩‍👧", "\r\n", "\n", "\t", "\u0378", "葛\U000E0100"];
        var texts = new List<string> { "", "\n", "\n\n", "abc \r\n\n", "日 本   ", "a" + new string('\u0301', 2048) };
        for (var sample = 0; sample < 80; sample++)
        {
            var text = new StringBuilder();
            for (var index = 0; index < random.Next(1, 40); index++) text.Append(elements[random.Next(elements.Length)]);
            texts.Add(text.ToString());
        }

        foreach (var text in texts)
        foreach (var size in new[] { 0d, 7.25, 11, 28 })
        foreach (var wrap in new[] { false, true })
        {
            var normalized = DisplayText.WithoutVariationSelectors(text);
            var boundary = context.Face.Measure(context.Face.Typeface.GetGlyphs("a日"), size);
            foreach (var width in new[] { -1d, 0, Math.BitDecrement(boundary), boundary, Math.BitIncrement(boundary), 37.5 })
            {
                var style = new FontStyle() with { Size = size };
                var actual = measurer.Layout(text, style, width, wrap);
                var expected = normalized.Length == 0 ? [] : TextLineBreaker.Break(normalized, width, wrap,
                    value => context.Face.Measure(context.Face.Typeface.GetGlyphs(value), size)).ToArray();
                Assert.Equal(expected.Length, actual.Lines.Count);
                for (var index = 0; index < expected.Length; index++)
                {
                    var line = actual.Lines[index];
                    var glyphs = context.Face.Typeface.GetGlyphs(expected[index].Text);
                    var sequence = new GlyphSequence(context.Face, glyphs, expected[index].Text, expected[index].Text);
                    Assert.Equal(expected[index].Text, line.Text);
                    Assert.Equal(expected[index].ExplicitBreak, line.ExplicitBreak);
                    Assert.Equal(context.Face.Measure(glyphs, size), line.Width);
                    Assert.Equal(sequence.Glyphs, line.Glyphs!.Glyphs);
                    Assert.Equal(sequence.Source, line.Glyphs.Source);
                    Assert.Equal(sequence.Clusters, line.Glyphs.Clusters);
                    var (ascent, descent, leading) = context.Metrics(size);
                    Assert.Equal(ascent, line.Baseline);
                    Assert.Equal(ascent + descent + leading, line.Height);
                    Assert.Equal(size, actual.EffectiveFontSize);
                }
            }
        }
    }

    [Fact]
    public void Invalid_utf16_retains_original_cluster_validation()
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        foreach (var text in new[] { new string((char)0xd800, 1), "a" + (char)0xdc00 + "b" })
        {
            var original = Record.Exception(() => new GlyphSequence(context.Face, context.Face.Typeface.GetGlyphs(text), text, text));
            var actual = Record.Exception(() => new SkiaTextMeasurer(context).Layout(text, new(), 100, true));
            Assert.Equal(original?.GetType(), actual?.GetType());
        }
    }

    [Fact]
    public void Long_outline_paragraph_selects_glyphs_once()
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var text = string.Concat(Enumerable.Repeat("日本語abc ", 2000));
            var result = new SkiaTextMeasurer(context).Layout(text, new FontStyle(), 50, true);
            Assert.True(result.Lines.Count > 100);
            Assert.Equal(1, metrics["textGlyphCalls"]);
            Assert.Equal(text.Length, metrics["textGlyphCharacters"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Cache_shares_only_measurement_inputs_and_remains_bounded()
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        var measurer = new SkiaTextMeasurer(context);
        var style = new FontStyle(Size: 11);
        var original = measurer.Layout("見出し", style, 10, false);
        Assert.Same(original, measurer.Layout("見出し", style with { Color = new(12, 34, 56), Underline = true }, 100, false));
        Assert.Same(original, measurer.Layout("見出し", style, -1, true));
        Assert.NotSame(original, measurer.Layout("見出し", style with { Size = 12 }, 100, false));
        Assert.NotSame(measurer.Layout("abc abc", style, 10, true), measurer.Layout("abc abc", style, 100, true));
        var maximumEntries = 0d;
        var maximumUnits = 0d;
        var evictions = 0d;
        var hits = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "layoutCacheEntries") maximumEntries = Math.Max(maximumEntries, value);
            if (name == "layoutCacheRetainedUnits") maximumUnits = Math.Max(maximumUnits, value);
            if (name == "layoutCacheEviction") evictions += value;
            if (name == "layoutCacheHit") hits += value;
        };
        try
        {
            for (var index = 0; index < 3000; index++)
            {
                measurer.Layout(new string('a', index % 2040) + index, style, 100, false);
                measurer.Layout("見出し", style, index, false);
            }

            Assert.InRange(maximumEntries, 1, 512);
            Assert.InRange(maximumUnits, 1, 131072);
            Assert.True(evictions > 0);
            Assert.True(hits > 2500);
            Assert.Equal(original.Size, measurer.Layout("見出し", style, 100, false).Size);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Planned_candidates_match_generic_selection_and_text_collection(bool titleRows, bool titleColumns)
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var measurer = new SkiaTextMeasurer(font);
        var border = new BorderStyle(new(1), new(1), new(1), new(1));
        var cells = new Dictionary<CellAddress, ReportCell>();
        for (var row = 12; row >= 1; row--)
        for (var column = 7; column >= 1; column--)
        {
            cells.Add(new(row, column), new((row + column) % 3 == 0 ? null : $"{row}:{column}", CellStyle.Default with
            {
                Background = new(200, 210, 220), Border = border, ShrinkToFit = true,
                Font = new(Size: (row + column) % 4 == 0 ? 0 : 10, Color: new(0, 0, 0, (byte)((row + column) % 2 == 0 ? 0 : 255))),
            }));
        }

        cells[new(5, 2)] = cells[new(5, 2)] with { RowSpan = 3, ColumnSpan = 3 };
        for (var row = 5; row <= 7; row++)
        for (var column = 2; column <= 4; column++)
        {
            if (row != 5 || column != 2) cells.Remove(new(row, column));
        }

        foreach (var order in new[] { PrintPageOrder.DownThenOver, PrintPageOrder.OverThenDown })
        {
            var sheet = new ReportSheet("test", cells, new Dictionary<int, ColumnDefinition> { [3] = new(IsHidden: true) },
                new Dictionary<int, RowDefinition> { [4] = new(IsHidden: true) }, [new(new(5, 2), new(7, 4))],
                new(200, 180, 10, 10, 10, 10, TitleRows: titleRows ? new(1, 2) : null, TitleColumns: titleColumns ? new(1, 2) : null)
                { PageOrder = order }, Images: [new(new(1, 1), 0, 0, 12, 12, TestSupport.Png())], HeaderFooter: new(new(Center: "header &P"), new(Right: "footer &N")));
            var context = new ReportLayoutContext(sheet, measurer);
            foreach (var pass in context.Policy.GetGeometryPasses()) pass.Execute(context);
            new TextMeasurePass().Execute(context);
            new CellBoundsPass().Execute(context);
            var plan = new SheetLayoutPlan(context);
            Assert.True(plan.Pages.Count > 1);
            var generator = new DrawCommandGeneratorPass();
            Assert.Throws<ArgumentOutOfRangeException>(() => plan.Build(0, 1, plan.Pages.Count, measurer, purpose: (PageBuildPurpose)99));
            Assert.Equal(PageBuildPurpose.Render, plan.Build(0, 1, plan.Pages.Count, measurer, purpose: PageBuildPurpose.Preflight).Purpose);
            for (var index = 0; index < plan.Pages.Count; index++)
            {
                var generic = PaginationPass.Materialize(context, plan.Pages[index], index + 1, plan.Pages.Count);
                var actual = plan.Build(index, index + 1, plan.Pages.Count, measurer);
                Assert.Equal(generic.Cells.Select(cell => cell.SourceAddress), actual.Cells.Select(cell => cell.SourceAddress));
                Assert.Equal(generic.Cells.Select(cell => cell.Bounds), actual.Cells.Select(cell => cell.Bounds));
                var headers = PaginationPass.GetHeaderFooterTexts(sheet, index + 1, plan.Pages.Count, DateTime.UnixEpoch);
                actual = actual with { HeaderFooterTexts = headers };
                var textOnly = plan.Build(index, index + 1, plan.Pages.Count, measurer, purpose: PageBuildPurpose.FontCollection) with { HeaderFooterTexts = headers };
                Assert.Equal(PageBuildPurpose.Render, actual.Purpose);
                Assert.Equal(PageBuildPurpose.FontCollection, textOnly.Purpose);
                Assert.Empty(textOnly.Images ?? []);
                Assert.Empty(textOnly.BorderOnlyCells);
                var fullTexts = generator.GeneratePage(actual, measurer).OfType<DrawTextCommand>().ToArray();
                var collected = generator.GenerateText(textOnly, measurer).Cast<DrawTextCommand>().ToArray();
                Assert.Equal(fullTexts.Select(text => (text.Text, text.Bounds, text.Style)), collected.Select(text => (text.Text, text.Bounds, text.Style)));
                Assert.Equal(fullTexts.SelectMany(text => text.TextLayout!.Lines).SelectMany(line => line.Glyphs!.Glyphs),
                    collected.SelectMany(text => text.TextLayout!.Lines).SelectMany(line => line.Glyphs!.Glyphs));
            }
        }
    }

    [Fact]
    public void Reused_paints_and_direct_positions_match_fresh_resources()
    {
        using var font = new SingleFontContext(TestSupport.JapaneseFont);
        var glyphs = font.Face.Typeface.GetGlyphs("日本abc 😀  ");
        var expected = new SkiaSharp.SKPoint[glyphs.Length];
        double x = 0;
        for (var index = 0; index < glyphs.Length; index++)
        {
            expected[index] = new((float)x, 0);
            x += font.Face.Measure(new[] { glyphs[index] }, 11.25);
        }
        var destination = new SkiaSharp.SKPoint[glyphs.Length];
        font.Face.WritePositions(glyphs, 11.25, destination);
        Assert.Equal(expected, destination);
        Assert.Throws<ArgumentException>(() => font.Face.WritePositions(glyphs, 11, new SkiaSharp.SKPoint[1]));
        using var reused = new SkiaSharp.SKBitmap(300, 300);
        using var fresh = new SkiaSharp.SKBitmap(300, 300);
        using var first = new SkiaSharp.SKCanvas(reused);
        using var second = new SkiaSharp.SKCanvas(fresh);
        using var resources = new SkiaPaintResources();
        first.Clear(SkiaSharp.SKColors.White);
        second.Clear(SkiaSharp.SKColors.White);
        for (var index = 0; index < 40; index++)
        {
            var side = new BorderSide((index % 3) + 0.5, new((byte)(index * 5), 20, 120), (BorderLineStyle)(index % 6));
            SkiaDrawingPrimitives.DrawStyledLine(first, side, 10, index * 7, 280, index * 7, resources: resources);
            SkiaDrawingPrimitives.DrawStyledLine(second, side, 10, index * 7, 280, index * 7);
            var color = new ReportColor(20, (byte)(index * 5), 90, 120);
            first.DrawRect(15, index * 7, 4, 4, resources.Fill(color));
            using var paint = SkiaDrawingPrimitives.CreatePaint(color, SkiaSharp.SKPaintStyle.Fill);
            second.DrawRect(15, index * 7, 4, 4, paint);
        }

        Assert.Equal(fresh.Pixels, reused.Pixels);
    }


    [Fact]
    public async Task Conversion_uses_one_payload_and_releases_paints_and_font_resources()
    {
        using var workbook = TestSupport.Workbook();
        using var output = new MemoryStream();
        var metrics = new Dictionary<string, double>();
        var maximumPayload = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            metrics[name] = metrics.GetValueOrDefault(name) + value;
            if (name == "pagePayloadActive") maximumPayload = Math.Max(maximumPayload, value);
        };
        try
        {
            var result = await ExcelConverter.ConvertAsync(workbook, output, TestSupport.Options);
            Assert.Equal(1, maximumPayload);
            Assert.Equal(result.PageCount, metrics["coreCommandBuild"]);
            Assert.Equal(result.PageCount, metrics["coreTextPageBuild"]);
            Assert.False(metrics.ContainsKey("pageCellIndexBuild"));
            Assert.False(metrics.ContainsKey("pageCellDeduplicateSort"));
            Assert.Equal(metrics["paintCreated"], metrics["paintDisposed"]);
            Assert.InRange(metrics["paintCreated"], 1, 2);
            Assert.Equal(metrics["typefaceCreated"], metrics["typefaceDisposed"]);
            Assert.Equal(metrics["pdfFontCreated"], metrics["pdfFontDisposed"]);
            Assert.Equal(metrics["pdfSubsetTypefaceCreated"], metrics["pdfSubsetTypefaceDisposed"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }


    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    public void Clock_evicts_only_the_required_entry_at_capacity(int count)
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        var measurer = new SkiaTextMeasurer(context);
        var entries = 0d;
        var evictions = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "layoutCacheEntries") entries = value;
            if (name == "layoutCacheEviction") evictions += value;
        };
        try
        {
            for (var index = 0; index < count; index++) measurer.Layout(index.ToString(), new(), 100, false);
            Assert.Equal(Math.Min(count, 512), entries);
            Assert.Equal(Math.Max(count - 512, 0), evictions);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

}
