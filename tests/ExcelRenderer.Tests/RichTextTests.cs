using ExcelRenderer.Abstractions;
using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using ExcelRenderer.SkiaSharp;
using ExcelRenderer.TestFixtures;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class RichTextTests
{
    [Fact]
    public void ReaderPreservesEffectiveRunsAndPlainText()
    {
        using var stream = RetainedWorkbook.Open("RichTextTests.ReaderPreservesEffectiveRunsAndPlainText.A4BF65835E997858.xlsx");
        stream.Position = 0;
        var cell = new ExcelReader().Read(stream).Sheets[0].Cells[new(1, 1)];
        Assert.Equal("firstsecond", cell.Text);
        Assert.Equal(cell.Text, string.Concat(cell.RichTextRuns!.Select(run => run.Text)));
        Assert.True(cell.RichTextRuns![0].Font.Bold);
        Assert.Equal(new ReportColor(255, 0, 0), cell.RichTextRuns![0].Font.Color);
        Assert.True(cell.RichTextRuns![1].Font.Italic);
        Assert.Equal(20, cell.RichTextRuns![1].Font.Size);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedAndInlineStringsPreserveThemeColorNewlinesAndVariationSelectors(bool shared)
    {
        using var stream = RetainedWorkbook.Open(shared ? "RichTextTests.SharedAndInlineStringsPreserveThemeColorNewlinesAndVariationSelectors.7AB65A77BFBD1A92.xlsx" : "RichTextTests.SharedAndInlineStringsPreserveThemeColorNewlinesAndVariationSelectors.EC937F47E279F52B.xlsx");
        stream.Position = 0;
        var cell = new ExcelReader().Read(stream).Sheets[0].Cells[new(1, 1)];
        Assert.Equal("漢\U000E0100\n後", cell.Text);
        Assert.Equal(cell.Text, string.Concat(cell.RichTextRuns!.Select(run => run.Text)));
        Assert.Equal(new ReportColor(255, 0, 0), cell.RichTextRuns![0].Font.Color);
        Assert.Equal(20, cell.RichTextRuns![1].Font.Size);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(255)]
    public void RotatedSingleDifferentlyStyledRunIsMarkedUnsupported(int rotation)
    {
        using var stream = RetainedWorkbook.Open(rotation switch { 45 => "RichTextTests.RotatedSingleDifferentlyStyledRunIsMarkedUnsupported.0F5B7F13737B81A7.xlsx", 255 => "RichTextTests.RotatedSingleDifferentlyStyledRunIsMarkedUnsupported.80CF2266E5A4DA38.xlsx", _ => throw new InvalidOperationException() });
        stream.Position = 0;
        var cell = new ExcelReader().Read(stream).Sheets[0].Cells[new(1, 1)];
        Assert.True(cell.UnsupportedRichTextFormatting);
        Assert.Equal("rotated", cell.Text);
    }

    [Fact]
    public void LayoutPreservesElementsAcrossFormattingBoundariesAndScalesEveryRun()
    {
        var fonts = new FontManager(new FontOptions());
        var measurer = new SkiaTextMeasurer(fonts);
        var small = new FontStyle(Size: 10, Color: new(255, 0, 0));
        var large = new FontStyle(Size: 20, Bold: true);
        var layout = measurer.Layout([new("a", small), new("\u0301b\r", large), new("\n\nC", large)], small, 1000, false);
        Assert.Equal(3, layout.Lines.Count);
        Assert.Equal("a\u0301b", layout.Lines[0].Text);
        Assert.Equal(small, layout.Lines[0].Runs[0].Font);
        Assert.Equal("a\u0301", layout.Lines[0].Runs[0].Run.SourceText);
        Assert.Contains(layout.Lines[0].Runs, run => run.Font == large);
        var scaled = TextLayoutTransform.Scale(layout, .5);
        Assert.Equal(5, scaled.Lines[0].Runs[0].Font!.Size);
        Assert.Contains(scaled.Lines[0].Runs, run => run.Font!.Size == 10);
        Assert.Equal(layout.Lines[0].Width * .5, scaled.Lines[0].Width);
        Assert.Equal(layout.Lines[0].Baseline * .5, scaled.Lines[0].Baseline);
    }

    [Fact]
    public void StyleBoundaryDoesNotForceWrapAndCacheIncludesColors()
    {
        var measurer = new SkiaTextMeasurer(new FontManager(new FontOptions()));
        var red = new FontStyle(Color: new(255, 0, 0));
        var blue = red with { Color = new(0, 0, 255) };
        var first = measurer.Layout([new("abc", red), new("def", blue)], red, 1000, true);
        var second = measurer.Layout([new("abc", blue), new("def", red)], red, 1000, true);
        Assert.Same(first, measurer.Layout([new("abc", red), new("def", blue)], red, 1000, true));
        Assert.Single(first.Lines);
        Assert.Equal("abcdef", first.Lines[0].Text);
        Assert.Equal(red, first.Lines[0].Runs[0].Font);
        Assert.Equal(blue, second.Lines[0].Runs[0].Font);
        var wrapped = measurer.Layout([new("abc", red), new("def", blue)], red, first.Size.Width / 2, true);
        Assert.True(wrapped.Lines.Count >= 2);
        Assert.Equal("abcdef", string.Concat(wrapped.Lines.Select(line => line.Text)));
    }

    [Fact]
    public void ManualModelValidationRejectsMismatchedTextAndSnapshotsRuns()
    {
        var runs = new List<CellTextRun> { new("A", new()) };
        var cell = new ReportCell("A", CellStyle.Default) { RichTextRuns = runs };
        var payload = FullCellTextData.Create(cell)!;
        runs[0] = new("B", new());
        Assert.Equal("A", payload.Runs[0].Text);
        Assert.Throws<ArgumentException>(() => FullCellTextData.Create(cell));
        Assert.Throws<ArgumentException>(() => FullCellTextData.Create(cell with { Text = "B", RichTextRuns = [new("B", new(Size: double.NaN))] }));
    }

    [Fact]
    public void PdfPngAndSerializedSvgUseRunSizesColorsAndBaseline()
    {
        var measurer = new SkiaTextMeasurer(new OutputFixture.FixedManager());
        var font = new FontStyle(Size: 10, Color: new(255, 0, 0));
        var large = font with { Size = 20, Color = new(0, 0, 255), Underline = true };
        var layout = measurer.Layout([new("A", font), new("B", large)], font, 100, false);
        var command = new ExcelRenderer.Drawing.DrawTextCommand(1, new(10, 10, 100, 50), "AB", CellStyle.Default with
        {
            Font = font,
            VerticalAlignment = VerticalAlignment.Top,
        }) { TextLayout = layout };
        using var output = new MemoryStream();
        new PdfRenderer(new OutputFixture.FixedManager()).Render([command], new(120, 80), output);
        var pdf = PdfContentProbe.Read(output.ToArray());
        Assert.Equal(2, pdf.Texts.Count);
        Assert.Equal(10, pdf.Texts[0].Size);
        Assert.Equal(20, pdf.Texts[1].Size);
        Assert.Equal(pdf.Texts[0].Origin.Y, pdf.Texts[1].Origin.Y, 4);
        Assert.Equal(pdf.Texts[0].Origin.X + layout.Lines[0].Runs[0].Advance, pdf.Texts[1].Origin.X, 2);
        using var images = new SerializedSvgProbe(command);
        images.Compare("rich-text-runs");
        Assert.Contains(images.Png.Pixels, pixel => pixel.Red > 200 && pixel.Blue < 80 && pixel.Green < 80);
        Assert.Contains(images.Png.Pixels, pixel => pixel.Blue > 200 && pixel.Red < 80 && pixel.Green < 80);
    }

    [Fact]
    public void SyntheticReaderFixtureReachesFinalizedPageRuns()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SampleInputs", "ReaderRichText", "representative.xlsx");
        var sheet = new ExcelReader().Read(path).Sheets[0];
        Assert.Equal(8, sheet.Cells.Count);
        Assert.All(sheet.Cells.Values, cell => Assert.Equal("Alpha 日本語 e\u0301 end", cell.Text));
        var layout = new ReportLayoutEngine(new SkiaTextMeasurer(new OutputFixture.FixedManager())).Layout(sheet);
        var cell = layout.Pages[0].Cells[0];
        Assert.NotNull(cell.TextLayout);
        Assert.Contains(cell.TextLayout!.Lines.SelectMany(line => line.Runs), run => run.Font?.Size == 16);
        Assert.Contains(cell.TextLayout.Lines.SelectMany(line => line.Runs), run => run.Font?.Color == new ReportColor(255, 0, 0));
    }

    [Fact]
    public void RepeatedTitlesRetainRunStylesAndApplyPrintScaleExactlyOnce()
    {
        var font = new FontStyle(Size: 12, Color: new(255, 0, 0));
        var title = new ReportCell("AB", new(font))
        {
            RichTextRuns = [new("A", font), new("B", font with { Size = 20, Underline = true })],
        };
        var cells = Enumerable.Range(1, 8).ToDictionary(row => new CellAddress(row, 1), row => row == 1 ? title : new ReportCell("body", CellStyle.Default));
        var sheet = new ReportSheet("titles", cells, new Dictionary<int, ColumnDefinition> { [1] = new(100) },
            Enumerable.Range(1, 8).ToDictionary(row => row, row => new RowDefinition(40)), [],
            new(200, 100, 0, 0, 0, 0, Scale: .5, TitleRows: new(1, 1)));
        var pages = new ReportLayoutEngine(new SkiaTextMeasurer(new OutputFixture.FixedManager())).Layout(sheet).Pages;
        Assert.True(pages.Count > 1);
        foreach (var page in pages)
        {
            var cell = Assert.Single(page.Cells, cell => cell.SourceAddress == new CellAddress(1, 1));
            var runs = cell.TextLayout!.Lines.SelectMany(line => line.Runs).ToArray();
            Assert.Equal(new double[] { 6, 10 }, runs.Select(run => run.Font!.Size));
            Assert.All(runs, run => Assert.Equal(font.Color, run.Font!.Color));
            Assert.True(runs[1].Font!.Underline);
        }
    }

    [Fact]
    public void ExistingConstructorsDeconstructorsAndLegacyLayoutServicesRemainUsable()
    {
        var cell = new ReportCell("AB", CellStyle.Default, 1, 1, null)
        {
            RichTextRuns = [new("A", new(Size: 20)), new("B", new(Size: 8))],
        };
        var (text, style, rows, columns, formula) = cell;
        Assert.Equal("AB", text);
        Assert.Equal(1, rows);
        Assert.Equal(1, columns);
        Assert.Null(formula);
        var oldRun = new TextLayoutRun(new("AB", OutputFixture.Face), 0, 10);
        var (resolved, x, advance) = oldRun;
        Assert.Equal("AB", resolved.Text);
        Assert.Equal(0, x);
        Assert.Equal(10, advance);
        var sheet = new ReportSheet("manual", new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = cell },
            new Dictionary<int, ColumnDefinition> { [1] = new(100) }, new Dictionary<int, RowDefinition> { [1] = new(40) }, [], new(200, 200));
        var measurer = new LegacyLayout();
        var page = new ReportLayoutEngine(measurer).Layout(sheet).Pages[0];
        Assert.Equal(style.Font, measurer.Font);
        Assert.Equal("AB", page.Cells[0].Cell.Text);
        Assert.Empty(page.Cells[0].TextLayout!.Lines[0].Runs);
    }

    private sealed class LegacyLayout : ITextMeasurer, ITextLayoutService
    {
        public FontStyle? Font { get; private set; }

        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => Layout(text, font, availableWidth, wrap).Size;

        public TextLayoutResult Layout(string text, FontStyle font, double availableWidth, bool wrap)
        {
            Font = font;
            return new(new(text.Length * 5, 10), [new(text, text.Length * 5, 10, 8, [], false)]);
        }
    }

}
