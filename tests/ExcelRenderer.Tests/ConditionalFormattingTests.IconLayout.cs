using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed partial class ConditionalFormattingTests
{
    [Theory]
    [InlineData(4, false, 15, 0, 0, true)]
    [InlineData(8, false, 15, 6, 8, false)]
    [InlineData(25, false, 2, 0, 0, true)]
    [InlineData(5, true, 15, 0, 0, true)]
    [InlineData(8, true, 15, 0, 0, true)]
    [InlineData(14, true, 15, 0, 0, true)]
    [InlineData(16, true, 15, 3, 5, true)]
    [InlineData(25, true, 15, 12, 14, true)]
    [InlineData(5, true, 2, 0, 0, true)]
    public void IconPlacementSharesWidthHeightAndVisibility(double width, bool showValue, double height, double size, double reserve, bool textVisible)
    {
        foreach (var rtl in new[] { false, true })
        {
            var placement = ConditionalIconPlacement.Resolve(new("3Arrows", 1, showValue, rtl), width, 11, height);
            Assert.Equal(size > 0, placement.CanDraw);
            Assert.Equal(size, placement.Size);
            Assert.Equal(rtl ? (0d, reserve) : (reserve, 0d), (placement.Left, placement.Right));
            Assert.Equal(textVisible, placement.ShowValue);
        }
    }

    [Theory]
    [InlineData(5, true, 15, false)]
    [InlineData(8, true, 15, false)]
    [InlineData(14, true, 15, false)]
    [InlineData(4, false, 15, false)]
    [InlineData(25, false, 2, false)]
    [InlineData(8, true, 15, true)]
    public void OmittedIconsMatchUndecoratedMeasurementAndInk(double width, bool showValue, double height, bool wrap)
    {
        using var input = Book(NumericRows(50), Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), $"showValue=\"{(showValue ? 1 : 0)}\"", ranges: "A1"));
        var source = Read(input);
        foreach (var rtl in new[] { false, true })
        {
            var original = source.Cells[new(1, 1)];
            var style = original.Style with { Font = original.Style.Font with { Size = 11 }, ShrinkToFit = !wrap, WrapText = wrap, Indent = 1 };
            var decorated = original with { Style = style, ConditionalVisual = original.ConditionalVisual! with { Icon = original.ConditionalVisual.Icon! with { RightToLeft = rtl } } };
            var ordinary = decorated with { ConditionalVisual = null };
            // Padding is 1pt total and one indent adds half the original 11pt em.
            var sheet = source with { Cells = new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = decorated }, Columns = new Dictionary<int, ColumnDefinition> { [1] = new(width + 6.5) }, Rows = new Dictionary<int, RowDefinition> { [1] = new(height) } };
            var control = sheet with { Cells = new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = ordinary } };
            var measurer = new SkiaSharp.SkiaTextMeasurer();
            var actualLayout = new ReportLayoutEngine(measurer).Layout(sheet);
            var expectedLayout = new ReportLayoutEngine(measurer).Layout(control);
            var actual = Assert.Single(actualLayout.Pages[0].Cells);
            var expected = Assert.Single(expectedLayout.Pages[0].Cells);
            Assert.Equal(width, Core.Layout.CellContentBounds.Calculate(CoreIntegration.CoreModelAdapter.ToCore(actual.Bounds), CoreIntegration.CoreModelAdapter.ToCore(style)).Width, 6);
            Assert.Equal(expected.ContentBounds, actual.ContentBounds);
            Assert.Equal(width, actual.ContentBounds.Width, 6);
            Assert.Equal(expected.TextLayout!.Size, actual.TextLayout!.Size);
            Assert.Equal(expected.TextLayout.EffectiveFontSize, actual.TextLayout.EffectiveFontSize);
            Assert.Equal(
                expected.TextLayout.Lines.Select(line => (line.Text, line.Width, line.Height, line.Baseline, line.Ascent, line.Descent, line.Leading, line.ExplicitBreak)),
                actual.TextLayout.Lines.Select(line => (line.Text, line.Width, line.Height, line.Baseline, line.Ascent, line.Descent, line.Leading, line.ExplicitBreak)));
            Assert.Equal(
                expected.TextLayout.Lines.SelectMany(line => line.Runs.Select(run => (run.Run.Text, run.X, run.Advance, run.Font))),
                actual.TextLayout.Lines.SelectMany(line => line.Runs.Select(run => (run.Run.Text, run.X, run.Advance, run.Font))));
            Assert.Equal(
                expected.TextLayout.Lines.SelectMany(line => line.Runs.SelectMany(run => run.Glyphs?.Glyphs ?? [])),
                actual.TextLayout.Lines.SelectMany(line => line.Runs.SelectMany(run => run.Glyphs?.Glyphs ?? [])));
            Assert.True(actual.TextLayout.EffectiveFontSize > 0);
            Assert.True(actual.TextLayout.Size.Width > 0);
            var generator = new DrawCommandGeneratorPass();
            var commands = generator.Generate(actualLayout);
            Assert.DoesNotContain(Flatten(commands), command => command is DrawPolygonCommand);
            Assert.Contains(Flatten(commands), command => command is DrawTextCommand);
            var continuous = generator.GenerateContinuous(new ContinuousLayoutPlan(sheet, measurer), measurer).ToArray();
            var continuousControl = generator.GenerateContinuous(new ContinuousLayoutPlan(control, measurer), measurer).ToArray();
            Assert.Equal(Assert.Single(Flatten(continuousControl).OfType<DrawTextCommand>()).TextLayout!.Size, Assert.Single(Flatten(continuous).OfType<DrawTextCommand>()).TextLayout!.Size);
            if (height > 2)
            {
                // Render text commands alone: borders and icons cannot satisfy the ink assertion.
                using var png = new MemoryStream();
                new SkiaSharp.PngRenderer().RenderPage(Flatten(commands).OfType<DrawTextCommand>().Cast<DrawCommand>().ToArray(), sheet.PageSettings, png, 144);
                using var bitmap = global::SkiaSharp.SKBitmap.Decode(png.ToArray());
                Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0 && pixel.Red < 180 && pixel.Green < 180 && pixel.Blue < 180);
                SavePhase3Artifact($"icon-omitted-{width}-{rtl}-{wrap}.png", png.ToArray());
            }
        }
    }

    [Theory]
    [InlineData(Rendering.OutputFormat.Png, Rendering.ImageLayoutMode.Paginated)]
    [InlineData(Rendering.OutputFormat.Png, Rendering.ImageLayoutMode.Continuous)]
    [InlineData(Rendering.OutputFormat.Svg, Rendering.ImageLayoutMode.Paginated)]
    [InlineData(Rendering.OutputFormat.Svg, Rendering.ImageLayoutMode.Continuous)]
    public async Task NarrowWorkbookValuesHaveInkInPagedAndContinuousOutputs(Rendering.OutputFormat format, Rendering.ImageLayoutMode mode)
    {
        using var input = Book(NumericRows(50), Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), ranges: "A1"));
        using (var package = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
        {
            var worksheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            worksheet.PrependChild(new DocumentFormat.OpenXml.Spreadsheet.Columns(new DocumentFormat.OpenXml.Spreadsheet.Column { Min = 1, Max = 1, Width = 12, CustomWidth = true }));
            package.WorkbookPart.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<DocumentFormat.OpenXml.Spreadsheet.CellFormat>().First().Alignment = new() { ShrinkToFit = true };
        }

        var options = new Excel.ExcelReadOptions { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 1 } };
        input.Position = 0;
        var sheet = new Excel.ExcelReader().Read(input, options).Sheets[0];
        Assert.Equal(9, sheet.Columns[1].Width, 6);
        var rendered = Assert.Single(new ReportLayoutEngine(new SkiaSharp.SkiaTextMeasurer()).Layout(sheet).Pages[0].Cells);
        Assert.Equal(8, rendered.ContentBounds.Width, 6);
        Assert.True(rendered.TextLayout!.EffectiveFontSize > 0);
        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, ImageLayout = mode, ReadOptions = options }, new Rendering.SingleStreamOutputSink(output));
        using var svg = new Svg.Skia.SKSvg();
        using var svgInput = new MemoryStream(output.ToArray());
        using var bitmap = format == Rendering.OutputFormat.Png ? global::SkiaSharp.SKBitmap.Decode(output.ToArray()) : Raster(svg.Load(svgInput)!);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0 && pixel.Red < 180 && pixel.Green < 180 && pixel.Blue < 180);
        SavePhase3Artifact($"narrow-value-{mode}.{format.ToString().ToLowerInvariant()}", output.ToArray());
        SavePhase3Artifact("narrow-value.xlsx", input.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IconOnlyNarrowCellsKeepTheirSixPointIconAndSuppressText(bool rtl)
    {
        using var input = Book(NumericRows(50), Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), "showValue=\"0\" iconSet=\"3Arrows\"", ranges: "A1"));
        var source = Read(input);
        var cell = source.Cells[new(1, 1)];
        cell = cell with { ConditionalVisual = cell.ConditionalVisual! with { Icon = cell.ConditionalVisual.Icon! with { RightToLeft = rtl } } };
        var sheet = source with { Cells = new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = cell }, Columns = new Dictionary<int, ColumnDefinition> { [1] = new(9) } };
        var layout = new ReportLayoutEngine(new SkiaSharp.SkiaTextMeasurer()).Layout(sheet);
        var rendered = Assert.Single(layout.Pages[0].Cells);
        Assert.Equal(0, rendered.ContentBounds.Width);
        var commands = Flatten(new DrawCommandGeneratorPass().Generate(layout)).ToArray();
        Assert.Equal(6, Assert.Single(commands.OfType<DrawPolygonCommand>()).Bounds.Width, 6);
        Assert.DoesNotContain(commands, command => command is DrawTextCommand);
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(2)]
    public void IconAndTextReservationsScaleOnceBeforeSelectionClipping(double scale)
    {
        using var input = Book(NumericRows(50), Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), "iconSet=\"3Arrows\"", ranges: "A1"));
        var source = Read(input);
        foreach (var rtl in new[] { false, true })
        {
            var original = source.Cells[new(1, 1)];
            var cell = original with { ColumnSpan = 2, Style = original.Style with { Font = original.Style.Font with { Size = 11 } }, ConditionalVisual = original.ConditionalVisual! with { Icon = original.ConditionalVisual.Icon! with { RightToLeft = rtl } } };
            var sheet = source with { Cells = new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = cell }, Columns = new Dictionary<int, ColumnDefinition> { [1] = new(8), [2] = new(9) }, MergedRanges = [new(new(1, 1), new(1, 2))], PageSettings = source.PageSettings with { Scale = scale } };
            var layout = new ReportLayoutEngine(new SkiaSharp.SkiaTextMeasurer()).Layout(sheet);
            var rendered = Assert.Single(layout.Pages[0].Cells);
            Assert.Equal(11 * scale, rendered.ContentBounds.Width, 6);
            var core = CorePageAdapter.ToCore(rendered);
            var placement = ConditionalCellVisualGeometry.IconPlacement(core);
            Assert.Equal(3, placement.Size, 6);
            Assert.Equal(5, placement.Left + placement.Right, 6);
            var clipped = core with { ClipBounds = new(core.Bounds.X + core.Bounds.Width - scale, core.Bounds.Y, scale, core.Bounds.Height) };
            Assert.Equal(placement, ConditionalCellVisualGeometry.IconPlacement(clipped));
            var selected = new ReportLayoutEngine(new SkiaSharp.SkiaTextMeasurer()).Layout(sheet with { RequestedRange = new(new(1, 2), new(1, 2)) });
            var selectedCell = Assert.Single(selected.Pages[0].Cells);
            Assert.Equal(placement, ConditionalCellVisualGeometry.IconPlacement(CorePageAdapter.ToCore(selectedCell)));
            Assert.Equal(rendered.ContentBounds.Width, selectedCell.ContentBounds.Width, 6);
            Assert.Equal(rendered.ContentBounds.X - rendered.Bounds.X, selectedCell.ContentBounds.X - selectedCell.Bounds.X, 6);
            var polygon = Assert.Single(Flatten(new DrawCommandGeneratorPass().Generate(layout)).OfType<DrawPolygonCommand>());
            Assert.Equal(3 * scale, polygon.Bounds.Width, 6);
            Assert.Equal(rtl ? rendered.Bounds.X + rendered.Bounds.Width - (4 * scale) : rendered.Bounds.X + scale, polygon.Bounds.X, 6);
        }
    }
}
