using ClosedXML.Excel;
using ExcelRenderer.Abstractions;
using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class RichTextTests
{
    [Fact]
    public void ReaderPreservesEffectiveRunsAndPlainText()
    {
        using var workbook = new XLWorkbook();
        var source = workbook.AddWorksheet("rich").Cell(1, 1);
        source.CreateRichText().AddText("first").SetBold().SetFontColor(XLColor.Red);
        source.GetRichText().AddText("second").SetItalic().SetFontSize(20);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var cell = new ExcelReader().Read(stream).Sheets[0].Cells[new(1, 1)];
        Assert.Equal("firstsecond", cell.Text);
        Assert.Equal(cell.Text, string.Concat(cell.RichTextRuns!.Select(run => run.Text)));
        Assert.True(cell.RichTextRuns![0].Font.Bold);
        Assert.Equal(new ReportColor(255, 0, 0), cell.RichTextRuns![0].Font.Color);
        Assert.True(cell.RichTextRuns![1].Font.Italic);
        Assert.Equal(20, cell.RichTextRuns![1].Font.Size);
    }

    [Fact]
    public void LayoutPreservesElementsAcrossFormattingBoundariesAndScalesEveryRun()
    {
        var fonts = new FontManager(new FontOptions());
        var measurer = new PdfSharpTextMeasurer(fonts);
        var small = new FontStyle(Size: 10, Color: new(255, 0, 0));
        var large = new FontStyle(Size: 20, Bold: true);
        var layout = measurer.Layout([new("a", small), new("\u0301b\n\nC", large)], small, 1000, false);
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
        var measurer = new PdfSharpTextMeasurer(new FontManager(new FontOptions()));
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
        var measurer = new PdfSharpTextMeasurer(new OutputFixture.FixedManager());
        var font = new FontStyle(Size: 10, Color: new(255, 0, 0));
        var large = font with { Size = 20, Color = new(0, 0, 255), Underline = true };
        var layout = measurer.Layout([new("A", font), new("B", large)], font, 100, false);
        var command = new ExcelRenderer.Drawing.DrawTextCommand(1, new(10, 10, 100, 50), "AB", CellStyle.Default with
        {
            Font = font,
            VerticalAlignment = VerticalAlignment.Top,
        }) { TextLayout = layout };
        using var output = new MemoryStream();
        new PdfSharpRenderer(new OutputFixture.FixedManager()).Render([command], new(120, 80), output);
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
        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer(new OutputFixture.FixedManager())).Layout(sheet);
        var cell = layout.Pages[0].Cells[0];
        Assert.NotNull(cell.TextLayout);
        Assert.Contains(cell.TextLayout!.Lines.SelectMany(line => line.Runs), run => run.Font?.Size == 16);
        Assert.Contains(cell.TextLayout.Lines.SelectMany(line => line.Runs), run => run.Font?.Color == new ReportColor(255, 0, 0));
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

    [Fact]
    public void ClosedXmlDirtyDependencyEvaluationCanReachUnrelatedSheet()
    {
        using var workbook = new XLWorkbook();
        var broken = workbook.AddWorksheet("unselected");
        broken.Cell(1, 1).FormulaA1 = "A1+1";
        var selected = workbook.AddWorksheet("selected");
        selected.Cell(1, 1).Value = 4;
        selected.Cell(1, 2).FormulaA1 = "A1+1";
        selected.Cell(1, 2).InvalidateFormula();
        Assert.Throws<InvalidOperationException>(() => selected.Evaluate("B1+1", "C1"));
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
