using System.Globalization;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed partial class ConditionalFormattingTests
{
    private const string X14Namespace = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string XmNamespace = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string LinkId = "{A3CBA125-C045-4C79-BDA7-A07FDC89F058}";

    [Theory]
    [InlineData(false, "none", 0, 100, 0, 10, 90, false, 0, .1, -1)]
    [InlineData(false, "none", 0, 100, 50, 10, 90, false, 0, .5, -1)]
    [InlineData(false, "none", 0, 100, 100, 10, 90, false, 0, .9, -1)]
    [InlineData(true, "automatic", 0, 100, 25, 0, 100, false, 0, .25, -1)]
    [InlineData(true, "automatic", -100, 0, -25, 0, 100, false, .75, 1, -1)]
    [InlineData(true, "automatic", -50, 100, -25, 0, 100, false, 1.0 / 6, 1.0 / 3, 1.0 / 3)]
    [InlineData(true, "automatic", -50, 100, 50, 0, 100, false, 1.0 / 3, 2.0 / 3, 1.0 / 3)]
    [InlineData(true, "middle", -50, 100, -50, 0, 100, false, .25, .5, .5)]
    [InlineData(true, "middle", -50, 100, 50, 0, 100, false, .5, .75, .5)]
    [InlineData(true, "none", -50, 100, 0, 0, 100, false, 0, 1.0 / 3, -1)]
    [InlineData(true, "automatic", 0, 100, 25, 0, 100, true, .75, 1, -1)]
    [InlineData(true, "automatic", -50, 100, -25, 10, 90, false, 1.0 / 6, 1.0 / 3, 1.0 / 3)]
    public void Phase3BarIntervalsMatchTheFixedProfile(bool extension, string axis, double lower, double upper, double value, int minLength, int maxLength, bool rtl, double start, double end, double line)
    {
        var points = Point("num", lower, extension) + Point("num", upper, extension);
        var attributes = $"minLength=\"{minLength}\" maxLength=\"{maxLength}\" axisPosition=\"{axis}\" direction=\"{(rtl ? "rightToLeft" : "leftToRight")}\"";
        using var input = Book(NumericRows(value), Bar(points, attributes, extension));
        var sheet = Read(input);
        var bar = Assert.IsType<ConditionalDataBar>(sheet.Cells[new(1, 1)].ConditionalVisual?.DataBar);
        Assert.InRange(Math.Abs(start - bar.Start), 0, 1e-6);
        Assert.InRange(Math.Abs(end - bar.End), 0, 1e-6);
        Assert.Equal(line < 0 ? null : line, bar.Axis);
        Assert.Equal(value < 0 && extension ? new Core.Model.ReportColor(255, 0, 0) : new Core.Model.ReportColor(0, 128, 255), bar.Fill);
    }

    [Theory]
    [InlineData("automatic", 0, 0, false)]
    [InlineData("middle", 0, 0, true)]
    [InlineData("automatic", 5, 1, false)]
    [InlineData("automatic", -5, 1, false)]
    public void AutoThresholdsHandleEqualPopulations(string axis, double value, double length, bool axisVisible)
    {
        using var input = Book(NumericRows(value, value), Bar(Point("autoMin", 0, true) + Point("autoMax", 0, true), $"axisPosition=\"{axis}\" minLength=\"0\" maxLength=\"100\" showValue=\"0\"", true));
        var sheet = Read(input);
        var visual = Assert.IsType<ConditionalCellVisual>(sheet.Cells[new(1, 1)].ConditionalVisual);
        Assert.True(visual.HideValue);
        Assert.Equal(length, visual.DataBar!.End - visual.DataBar.Start, 6);
        Assert.Equal(axisVisible, visual.DataBar.Axis is not null);
        Assert.DoesNotContain(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingThresholdInvalid");
    }

    [Theory]
    [InlineData(32, true, false, 0)]
    [InlineData(33, true, false, 1)]
    [InlineData(66, true, false, 1)]
    [InlineData(67, true, false, 2)]
    [InlineData(33, false, false, 0)]
    [InlineData(67, false, false, 1)]
    [InlineData(32, true, true, 2)]
    [InlineData(67, true, true, 0)]
    public void IconBoundariesPreserveInclusivityAndReverse(double value, bool gte, bool reverse, int expected)
    {
        var points = Point("num", 0) + Point("num", 33, gte: gte) + Point("num", 67, gte: gte);
        using var input = Book(NumericRows(value), Icons(points, $"reverse=\"{(reverse ? 1 : 0)}\""));
        Assert.Equal(expected, Read(input).Cells[new(1, 1)].ConditionalVisual?.Icon?.Index);
    }

    [Theory]
    [InlineData("percent", 0)]
    [InlineData("percentile", 1)]
    public void IconsUseIndividualThresholdTypesAndSharedPopulation(string type, int expected)
    {
        using var input = Book(NumericRows(0, 1, 2, 100), Icons(Point("num", -100) + Point(type, 50) + Point("num", 101), "percent=\"0\""));
        Assert.Equal(expected, Read(input).Cells[new(3, 1)].ConditionalVisual?.Icon?.Index);
    }

    [Fact]
    public void EqualIconBoundariesChooseTheHighestMatchingIcon()
    {
        using var input = Book(NumericRows(5), Icons(Point("num", 5) + Point("num", 5) + Point("num", 5)));
        Assert.Equal(2, Read(input).Cells[new(1, 1)].ConditionalVisual?.Icon?.Index);
    }

    [Theory]
    [InlineData("4Arrows", "")]
    [InlineData("3Flags", "")]
    [InlineData("3TrafficLights2", "")]
    [InlineData("3Arrows", "custom=\"1\"")]
    public void UnsupportedIconsRetainValuesAndReportAStableCode(string set, string attributes)
    {
        using var input = Book(NumericRows(5), Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), $"iconSet=\"{set}\" {attributes} showValue=\"0\""));
        var sheet = ReadPhase3WithFeatures(input);
        Assert.Null(sheet.Cells[new(1, 1)].ConditionalVisual);
        Assert.Contains(sheet.UnsupportedFeatures, feature => feature.Code == "ConditionalFormattingIconSetUnsupported");
        Assert.Equal("5", sheet.Cells[new(1, 1)].Text);
    }

    [Fact]
    public void LinkedGuidRulesNormalizeOnceAndKeepTheLegacyPositiveColor()
    {
        var legacy = Rule("dataBar", "<dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF0080FF\"/></dataBar>" + $"<extLst><ext uri=\"{{B025F937-C7B1-47D3-B67F-A62EFF666E3E}}\"><x14:id xmlns:x14=\"{X14Namespace}\">{LinkId.ToLowerInvariant().Trim('{', '}')}</x14:id></ext></extLst>", dxf: null, ranges: "A1:A2");
        var extension = Bar(Point("num", 0, true) + Point("num", 100, true), "minLength=\"0\" maxLength=\"100\"", true, priority: null, ranges: "A1:A2", linked: true);
        using var input = Book(NumericRows(25, 100), extension + legacy);
        using (var reader = OpenXmlWorkbookReader.Open(input))
        {
            Assert.Equal(2, reader.ReadWorksheetInfo(1).ConditionalFormattingRules.Count);
        }

        var sheet = Read(input);
        Assert.Equal(.25, sheet.Cells[new(1, 1)].ConditionalVisual?.DataBar?.End);
        Assert.DoesNotContain(sheet.UnsupportedFeatures, feature => feature.Code is "ConditionalFormattingPriorityInvalid" or "ConditionalFormattingExtensionUnsupported");
    }

    [Fact]
    public void VisualsComposeInTheExistingPriorityPassAndFailureRollsBackEverything()
    {
        var bars = Bar(Point("num", 0) + Point("num", 100), "showValue=\"0\"");
        var icons = Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), priority: 2);
        using var input = Book(NumericRows(50), bars + icons + Rule("expression", "<formula>TRUE</formula>", priority: 3));
        var cell = Read(input).Cells[new(1, 1)];
        Assert.NotNull(cell.ConditionalVisual?.DataBar);
        Assert.NotNull(cell.ConditionalVisual?.Icon);
        Assert.NotNull(cell.Style.Background);
        using var broken = Book(NumericRows(50), bars + Rule("timePeriod", "", priority: 2));
        Assert.Null(Read(broken).Cells[new(1, 1)].ConditionalVisual);
        using var stopped = Book(NumericRows(50), Rule("expression", "<formula>TRUE</formula>", extra: "stopIfTrue=\"1\"") + Bar(Point("num", 0) + Point("num", 100), priority: 2));
        Assert.Null(Read(stopped).Cells[new(1, 1)].ConditionalVisual);
    }

    [Fact]
    public void TenThousandCellsShareOnePopulationAndAtMostOnePercentileSort()
    {
        using var input = Book(NumericRows(Enumerable.Range(0, 10000).Select(value => (double)value).ToArray()),
            Bar(Point("min", 0) + Point("max", 0)) + Icons(Point("num", 0) + Point("percentile", 33) + Point("percentile", 67), priority: 2));
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var sheet = Read(input);
            Assert.Equal(10000, sheet.Cells.Values.Count(cell => cell.ConditionalVisual?.Icon is not null && cell.ConditionalVisual.DataBar is not null));
            Assert.Equal(1, metrics["conditionalFormatting.populations"]);
            Assert.Equal(1, metrics["conditionalFormatting.sorts"]);
            Assert.Equal(10000, metrics["conditionalFormatting.valueReads"]);
            Assert.Equal(10000, Assert.Single(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingDataBarGradientApproximated").OccurrenceCount);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Theory]
    [InlineData("3Arrows")]
    [InlineData("3ArrowsGray")]
    [InlineData("3TrafficLights1")]
    [InlineData("3Symbols")]
    [InlineData("3Symbols2")]
    public async Task AllIconAssetsReachEveryVisualOutputAndMapping(string set)
    {
        var rules = Bar(Point("num", 0) + Point("num", 100), ranges: "A1") + Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), $"iconSet=\"{set}\" showValue=\"0\"", priority: 2, ranges: "A1");
        using var template = Book("<row r=\"1\" ht=\"28\" customHeight=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>**items[*]</t></is></c></row>", rules);
        using var mapped = new MemoryStream();
        Mapping.ExcelTemplateMapper.Map(template, mapped, new { items = new[] { 0, 50, 100 } });
        var sheet = Read(mapped);
        Assert.Equal(new[] { 0, 1, 2 }, sheet.Cells.OrderBy(entry => entry.Key.Row).Select(entry => entry.Value.ConditionalVisual!.Icon!.Index));
        foreach (var format in new[] { OutputFormat.Pdf, OutputFormat.Png, OutputFormat.Svg, OutputFormat.PostScript, OutputFormat.Markdown })
        {
            mapped.Position = 0;
            using var output = new MemoryStream();
            await ExcelConverter.RenderAsync(mapped, new() { OutputFormat = format }, new SingleStreamOutputSink(output));
            Assert.True(output.Length > 50);
            if (format == OutputFormat.Png)
            {
                using var bitmap = global::SkiaSharp.SKBitmap.Decode(output.ToArray());
                var lowColor = set == "3ArrowsGray" ? new global::SkiaSharp.SKColor(89, 89, 89) : new global::SkiaSharp.SKColor(192, 80, 77);
                Assert.Contains(bitmap.Pixels, pixel => pixel == lowColor);
            }

            if (format == OutputFormat.Markdown)
            {
                Assert.Contains("100", System.Text.Encoding.UTF8.GetString(output.ToArray()));
            }

            SavePhase3Artifact($"{set}.{format.ToString().ToLowerInvariant()}", output.ToArray());
        }
    }

    [Fact]
    public void PublicLayoutPreservesPayloadAndDrawsDecorationsBetweenFillsAndBorders()
    {
        using var input = Book(NumericRows(50), Rule("expression", "<formula>TRUE</formula>") + Bar(Point("num", 0) + Point("num", 100), "showValue=\"0\"", priority: 2) + Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), "iconSet=\"3Arrows\"", priority: 3));
        var sheet = Read(input);
        var measurer = new SkiaSharp.SkiaTextMeasurer();
        var layout = new ReportLayoutEngine(measurer).Layout(sheet);
        var commands = new DrawCommandGeneratorPass().Generate(layout);
        Assert.IsType<FillRectangleCommand>(commands[0]);
        Assert.Contains(Flatten(commands), command => command is DrawPolygonCommand);
        Assert.DoesNotContain(Flatten(commands), command => command is DrawTextCommand text && text.Text == "50");
        Assert.NotNull(layout.Pages[0].Cells[0].Cell.ConditionalVisual);
        Assert.Equal("50", layout.Pages[0].Cells[0].Cell.Text);
    }

    [Fact]
    public void NarrowCellsRestoreHiddenValues()
    {
        using var input = Book(NumericRows(50), Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), "showValue=\"0\""));
        var source = Read(input).Cells[new(1, 1)];
        var cell = new RenderCell(source, new(0, 0, 4, 15));
        var commands = new DrawCommandGeneratorPass().Generate(new([new RenderPage(1, [cell])]));
        Assert.Contains(Flatten(commands), command => command is DrawTextCommand);
    }

    [Fact]
    public void OrphanExtensionsDoNotInvalidateIndependentOrdinaryRules()
    {
        using var input = Book(NumericRows(25), Rule("expression", "<formula>TRUE</formula>") + Bar(Point("num", 0, true) + Point("num", 100, true), extension: true, priority: null));
        var sheet = ReadPhase3WithFeatures(input);
        Assert.NotNull(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Null(sheet.Cells[new(1, 1)].ConditionalVisual);
        Assert.Contains(sheet.UnsupportedFeatures, feature => feature.Code == "ConditionalFormattingExtensionLinkInvalid");
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("=5", true)]
    [InlineData("$A$2", true)]
    [InlineData("A2", false)]
    [InlineData("$A2", false)]
    [InlineData("A$2", false)]
    public void ExtendedFormulaThresholdsRequireAbsoluteReferences(string expression, bool valid)
    {
        var lower = $"<x14:cfvo type=\"formula\"><xm:f>{expression}</xm:f></x14:cfvo>";
        using var input = Book(NumericRows(50, 0), Bar(lower + Point("num", 100, true), "showValue=\"0\"", true));
        var sheet = ReadPhase3WithFeatures(input);
        Assert.Equal(valid, sheet.Cells[new(1, 1)].ConditionalVisual is not null);
        if (valid)
        {
            Assert.Equal(.5, sheet.Cells[new(1, 1)].ConditionalVisual!.DataBar!.End, 6);
        }
        if (!valid)
        {
            Assert.Contains(sheet.UnsupportedFeatures, feature => feature.Code == "ConditionalFormattingThresholdInvalid");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtendedMappingPreservesTheGuidPairAndAbsoluteFormula(bool independent)
    {
        var extension = Bar("<x14:cfvo type=\"formula\"><xm:f>$B$2</xm:f></x14:cfvo>" + Point("num", 100, true), "gradient=\"0\"", true, independent ? 1 : null, "A1", !independent);
        var legacy = independent ? "" : Rule("dataBar", "<dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF0080FF\"/></dataBar>" + $"<extLst><ext uri=\"{{B025F937-C7B1-47D3-B67F-A62EFF666E3E}}\"><x14:id xmlns:x14=\"{X14Namespace}\">{LinkId}</x14:id></ext></extLst>", dxf: null, ranges: "A1");
        using var template = Book("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>**items[*]</t></is></c></row><row r=\"2\"><c r=\"B2\"><v>0</v></c></row>", legacy + extension);
        using var mapped = new MemoryStream();
        Mapping.ExcelTemplateMapper.Map(template, mapped, new { items = new[] { 25, 50, 75 } });
        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(mapped.ToArray())))
        {
            using var xml = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var root = XElement.Load(xml);
            XNamespace xm = XmNamespace;
            Assert.Equal("$B$4", root.Descendants(xm + "f").First().Value);
            Assert.Equal("A1:A3", root.Descendants(xm + "sqref").Single().Value);
            Assert.Contains(root.Descendants().Attributes("id"), attribute => attribute.Value == LinkId);
        }

        var sheet = ReadPhase3WithFeatures(mapped);
        Assert.NotNull(sheet.Cells[new(1, 1)].ConditionalVisual?.DataBar);
        Assert.DoesNotContain(sheet.UnsupportedFeatures, feature => feature.Code is "ConditionalFormattingExtensionLinkInvalid" or "ConditionalFormattingPriorityInvalid");
        foreach (var format in new[] { OutputFormat.Pdf, OutputFormat.Png, OutputFormat.Svg, OutputFormat.PostScript })
        {
            mapped.Position = 0;
            using var output = new MemoryStream();
            await ExcelConverter.RenderAsync(mapped, new() { OutputFormat = format }, new SingleStreamOutputSink(output));
            Assert.True(output.Length > 100);
        }
    }

    [Theory]
    [InlineData("automatic", true)]
    [InlineData("automatic", false)]
    [InlineData("middle", true)]
    [InlineData("middle", false)]
    public void SavedSheetViewControlsContextDirection(string axis, bool rtl)
    {
        using var input = Book(NumericRows(25), Bar(Point("num", 0, true) + Point("num", 100, true), $"minLength=\"0\" maxLength=\"100\" axisPosition=\"{axis}\"", true));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.PrependChild(new DocumentFormat.OpenXml.Spreadsheet.SheetViews(new DocumentFormat.OpenXml.Spreadsheet.SheetView { WorkbookViewId = 0, RightToLeft = rtl }));
        }

        var bar = Read(input).Cells[new(1, 1)].ConditionalVisual!.DataBar!;
        Assert.Equal(axis == "middle" ? rtl ? .375 : .5 : rtl ? .75 : 0, bar.Start, 6);
        Assert.Equal(axis == "middle" ? rtl ? .5 : .625 : rtl ? 1 : .25, bar.End, 6);
    }

    [Fact]
    public void PagedAndContinuousCommandsShareTheSameDecorationGeometry()
    {
        using var input = Book(NumericRows(50), Bar(Point("num", 0) + Point("num", 100)) + Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), "iconSet=\"3Arrows\"", priority: 2));
        var sheet = Read(input);
        var measurer = new SkiaSharp.SkiaTextMeasurer();
        var generator = new DrawCommandGeneratorPass();
        var paged = generator.Generate(new ReportLayoutEngine(measurer).Layout(sheet));
        var continuous = generator.GenerateContinuous(new ContinuousLayoutPlan(sheet, measurer), measurer).ToArray();
        var pagedPolygon = Assert.Single(Flatten(paged).OfType<DrawPolygonCommand>());
        var continuousPolygon = Assert.Single(Flatten(continuous).OfType<DrawPolygonCommand>());
        Assert.Equal(pagedPolygon.Bounds.Width, continuousPolygon.Bounds.Width, 6);
        Assert.Equal(pagedPolygon.Bounds.Height, continuousPolygon.Bounds.Height, 6);
        Assert.Equal(pagedPolygon.Vertices.Select(point => (Math.Round(point.X - pagedPolygon.Bounds.X, 6), Math.Round(point.Y - pagedPolygon.Bounds.Y, 6))), continuousPolygon.Vertices.Select(point => (Math.Round(point.X - continuousPolygon.Bounds.X, 6), Math.Round(point.Y - continuousPolygon.Bounds.Y, 6))));
    }

    [Theory]
    [InlineData("legacy-bars")]
    [InlineData("x14-bars")]
    [InlineData("icons")]
    [InlineData("layout-mapping")]
    public async Task RetainedPhase3FixturesRenderWithValidatedMetadata(string name)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/validation/conditional-formatting-phase3/fixtures", name + ".xlsx"));
        using var original = new MemoryStream(File.ReadAllBytes(path));
        using var input = new MemoryStream();
        if (name == "layout-mapping")
        {
            Mapping.ExcelTemplateMapper.Map(original, input, new { items = new[] { -25, 0, 50, 100 } });
        }
        else
        {
            original.CopyTo(input);
        }

        var sheet = ReadPhase3WithFeatures(input);
        Assert.Contains(sheet.Cells.Values, cell => cell.ConditionalVisual is not null);
        Assert.DoesNotContain(sheet.UnsupportedFeatures, feature => feature.Code.StartsWith("ConditionalFormatting", StringComparison.Ordinal));
        SavePhase3Artifact(name + ".xlsx", input.ToArray());
        foreach (var format in new[] { OutputFormat.Pdf, OutputFormat.Png, OutputFormat.Svg, OutputFormat.PostScript })
        {
            input.Position = 0;
            using var output = new MemoryStream();
            var result = await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, DiagnosticOptions = new() { DetectUnsupportedFeatures = true } }, new SingleStreamOutputSink(output));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("ConditionalFormatting", StringComparison.Ordinal) && diagnostic.Code != "ConditionalFormattingDataBarGradientApproximated");
            SavePhase3Artifact(name + "." + (format == OutputFormat.PostScript ? "ps" : format.ToString().ToLowerInvariant()), output.ToArray());
            if (format is OutputFormat.Png or OutputFormat.Svg)
            {
                input.Position = 0;
                using var canvas = new MemoryStream();
                await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, ImageLayout = ImageLayoutMode.Continuous }, new SingleStreamOutputSink(canvas));
                SavePhase3Artifact(name + "-continuous." + format.ToString().ToLowerInvariant(), canvas.ToArray());
            }
        }
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(8, 0)]
    [InlineData(14, 0)]
    [InlineData(16, 5)]
    [InlineData(25, 14)]
    public void VisibleValueIconInsetsPreserveOneOriginalEm(double width, double expected)
    {
        var visual = new ConditionalCellVisual(null, new("3Arrows", 1, true, false));
        var cell = new Core.Model.ReportCell("50", Core.Model.CellStyle.Default with { Font = Core.Model.CellStyle.Default.Font with { Size = 11 } }) { ExtensionData = new FullCellPresentationData(null, visual) };
        var policy = new FullLayoutPolicy();
        Assert.Equal((expected, 0d), policy.GetCellContentInsets(cell, width));
    }

    [Fact]
    public async Task GradientWarningsObeyStrictMode()
    {
        using var input = Book(NumericRows(50), Bar(Point("num", 0) + Point("num", 100)));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Svg, DiagnosticOptions = new() { StrictMode = true } }, new SingleStreamOutputSink(output)));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("min", "max", "5,5")]
    [InlineData("num", "num", "0,0")]
    public void InvalidCollapsedBarsNeverHideValues(string low, string high, string numbers)
    {
        using var input = Book(NumericRows(numbers), Bar(Point(low, 5) + Point(high, 5), "showValue=\"0\""));
        var sheet = Read(input);
        Assert.All(sheet.Cells.Values, cell => Assert.Null(cell.ConditionalVisual));
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingThresholdInvalid");
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("duplicate")]
    [InlineData("type")]
    [InlineData("color")]
    [InlineData("namespace")]
    [InlineData("namespace-list")]
    public void InvalidExtensionLinksFailConservatively(string failure)
    {
        var legacy = Rule(failure == "type" ? "expression" : "dataBar", "<dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF0080FF\"/></dataBar>" + $"<extLst><ext uri=\"{{B025F937-C7B1-47D3-B67F-A62EFF666E3E}}\"><x14:id xmlns:x14=\"{X14Namespace}\">{LinkId}</x14:id></ext></extLst>", dxf: null, ranges: "A1:A2");
        if (failure == "namespace")
        {
            legacy = legacy.Replace("<ext ", "<bad:ext xmlns:bad=\"urn:fake\" ", StringComparison.Ordinal).Replace("</ext>", "</bad:ext>", StringComparison.Ordinal);
        }
        else if (failure == "namespace-list")
        {
            legacy = legacy.Replace("<extLst>", "<bad:extLst xmlns:bad=\"urn:fake\">", StringComparison.Ordinal).Replace("</extLst>", "</bad:extLst>", StringComparison.Ordinal);
        }

        var extension = Bar(Point("num", 0, true) + Point("num", 100, true), extension: true, priority: null, ranges: failure == "overlap" ? "A1" : "A1:A2", linked: failure != "color");
        using var input = Book(NumericRows(25, 100), legacy + extension + (failure == "duplicate" ? extension : ""));
        var sheet = ReadPhase3WithFeatures(input);
        Assert.All(sheet.Cells.Values, cell => Assert.Null(cell.ConditionalVisual));
        Assert.Contains(sheet.UnsupportedFeatures, feature => feature.Code == (failure == "color" ? "ConditionalFormattingAttributesInvalid" : failure.StartsWith("namespace", StringComparison.Ordinal) ? "ConditionalFormattingExtensionUnsupported" : "ConditionalFormattingExtensionLinkInvalid"));
    }

    [Theory]
    [InlineData("minLength=\"101\"")]
    [InlineData("minLength=\"90\" maxLength=\"10\"")]
    [InlineData("showValue=\"yes\"")]
    [InlineData("direction=\"unknown\"")]
    public void InvalidVisualAttributesNeverSuppressText(string attributes)
    {
        using var input = Book(NumericRows(50), Bar(Point("num", 0, true) + Point("num", 100, true), attributes, true));
        var sheet = ReadPhase3WithFeatures(input);
        Assert.Null(sheet.Cells[new(1, 1)].ConditionalVisual);
        Assert.Contains(sheet.UnsupportedFeatures, feature => feature.Code == "ConditionalFormattingAttributesInvalid");
    }

    [Fact]
    public void VisualPayloadPreservesRichTextAndNormalFontComposition()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><r><rPr><b/></rPr><t>50</t></r></is></c><c r=\"B1\"><v>50</v></c></row>", Rule("expression", "<formula>TRUE</formula>", dxf: 1) + Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), priority: 2, ranges: "A1:B1"), [Yellow, Red]);
        // Rich text is typed text and receives font changes, but no numeric visual.
        var sheet = Read(input);
        Assert.Null(sheet.Cells[new(1, 1)].ConditionalVisual);
        Assert.True(sheet.Cells[new(1, 1)].RichTextRuns![0].Font.Bold);
        Assert.Equal(new ReportColor(255, 0, 0), sheet.Cells[new(1, 1)].RichTextRuns![0].Font.Color);
        var ordinary = sheet.Cells[new(1, 1)] with { ConditionalVisual = sheet.Cells[new(1, 2)].ConditionalVisual };
        // An explicit public model round trip also retains both immutable components.
        var layout = new ReportLayoutEngine(new SkiaSharp.SkiaTextMeasurer()).Layout(sheet with { Cells = new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = ordinary } });
        var rendered = layout.Pages[0].Cells[0].Cell;
        Assert.NotNull(rendered.ConditionalVisual);
        Assert.True(rendered.RichTextRuns![0].Font.Bold);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneExtendedIconSetsUseTheSavedThresholdTypes(bool reverse)
    {
        var payload = $"<x14:iconSet percent=\"0\" reverse=\"{(reverse ? 1 : 0)}\">{Point("num", 0, true)}{Point("num", 33, true)}{Point("num", 67, true)}</x14:iconSet>";
        var xml = $"<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{{78C0D931-6437-407d-A8EE-F0AAD7539E65}}\"><x14:conditionalFormattings xmlns:x14=\"{X14Namespace}\" xmlns:xm=\"{XmNamespace}\"><x14:conditionalFormatting><x14:cfRule type=\"iconSet\" priority=\"1\">{payload}</x14:cfRule><xm:sqref>A1:A3</xm:sqref></x14:conditionalFormatting></x14:conditionalFormattings></ext></extLst>";
        using var input = Book(NumericRows(32, 33, 67), xml);
        var sheet = Read(input);
        Assert.Equal(reverse ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 }, Enumerable.Range(1, 3).Select(row => sheet.Cells[new(row, 1)].ConditionalVisual!.Icon!.Index));
        Assert.All(sheet.Cells.Values, cell => Assert.Equal("3TrafficLights1", cell.ConditionalVisual?.Icon?.Set));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExtendedBarFillAndBorderSharingAreIndependent(bool sameFill, bool sameBorder)
    {
        var rule = Bar(Point("num", -100, true) + Point("num", 0, true), $"border=\"1\" negativeBarColorSameAsPositive=\"{(sameFill ? 1 : 0)}\" negativeBarBorderColorSameAsPositive=\"{(sameBorder ? 1 : 0)}\"", true)
            .Replace("</x14:dataBar>", "<x14:borderColor rgb=\"FF112233\"/><x14:negativeBorderColor rgb=\"FF445566\"/></x14:dataBar>", StringComparison.Ordinal);
        using var input = Book(NumericRows(-25), rule);
        var bar = Read(input).Cells[new(1, 1)].ConditionalVisual!.DataBar!;
        Assert.Equal(sameFill ? new Core.Model.ReportColor(0, 128, 255) : new Core.Model.ReportColor(255, 0, 0), bar.Fill);
        Assert.Equal(sameBorder ? new Core.Model.ReportColor(17, 34, 51) : new Core.Model.ReportColor(68, 85, 102), bar.Border);
    }

    [Theory]
    [InlineData("axis")]
    [InlineData("negative")]
    [InlineData("active")]
    [InlineData("namespace")]
    public void InvalidExtendedPayloadsDoNotHideValues(string invalid)
    {
        var rule = Bar(Point("num", 0, true) + Point("num", 100, true), "showValue=\"0\"", true);
        rule = invalid switch
        {
            "axis" => rule.Replace("<x14:axisColor rgb=\"FF000000\"/>", "", StringComparison.Ordinal),
            "negative" => rule.Replace("<x14:negativeFillColor rgb=\"FFFF0000\"/>", "", StringComparison.Ordinal),
            "active" => rule.Replace("</x14:cfRule>", "<xm:f>1</xm:f></x14:cfRule>", StringComparison.Ordinal),
            _ => rule.Replace("<x14:dataBar", "<bad:dataBar xmlns:bad=\"urn:fake\"", StringComparison.Ordinal).Replace("</x14:dataBar>", "</bad:dataBar>", StringComparison.Ordinal),
        };
        using var input = Book(NumericRows(50), rule);
        var sheet = ReadPhase3WithFeatures(input);
        Assert.Null(sheet.Cells[new(1, 1)].ConditionalVisual);
        Assert.Contains(sheet.UnsupportedFeatures, feature => feature.Code == "ConditionalFormattingAttributesInvalid");
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(1)]
    [InlineData(2)]
    public void MergedBarGeometryUsesTheFullCellAndScalesConstantsOnce(double scale)
    {
        using var input = Book(NumericRows(50), Bar(Point("num", 0) + Point("num", 100)) + Icons(Point("num", 0) + Point("num", 33) + Point("num", 67), "iconSet=\"3Arrows\"", priority: 2));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.Append(new DocumentFormat.OpenXml.Spreadsheet.MergeCells(new DocumentFormat.OpenXml.Spreadsheet.MergeCell { Reference = "A1:B1" }));
        }

        var sheet = Read(input);
        var layout = new ReportLayoutEngine(new SkiaSharp.SkiaTextMeasurer()).Layout(sheet with { PageSettings = sheet.PageSettings with { Scale = scale } });
        var rendered = layout.Pages[0].Cells.Single();
        Assert.Equal(2, rendered.Cell.ColumnSpan);
        var commands = Flatten(new DrawCommandGeneratorPass().Generate(layout)).ToArray();
        var bar = Assert.Single(commands.OfType<FillRectangleCommand>());
        Assert.Equal((rendered.Bounds.Width - (2 * scale)) * .5, bar.Bounds.Width, 6);
        Assert.Equal(rendered.Bounds.Height - (2 * scale), bar.Bounds.Height, 6);
        Assert.Equal(12 * scale, Assert.Single(commands.OfType<DrawPolygonCommand>()).Bounds.Width, 6);
    }

    [Fact]
    public void MalformedAndEmptyGuidExtensionsNeverCollideInTheLinkIndex()
    {
        var orphan = Bar(Point("num", 0, true) + Point("num", 100, true), extension: true, priority: null);
        var invalid = orphan.Replace(LinkId, "not-a-guid", StringComparison.Ordinal);
        var empty = orphan.Replace(LinkId, "00000000-0000-0000-0000-000000000000", StringComparison.Ordinal);
        using var input = Book(NumericRows(25), Rule("expression", "<formula>TRUE</formula>") + invalid + empty);
        var sheet = ReadPhase3WithFeatures(input);
        Assert.NotNull(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Null(sheet.Cells[new(1, 1)].ConditionalVisual);
        Assert.Equal(2, sheet.UnsupportedFeatures.Count(feature => feature.Code == "ConditionalFormattingExtensionLinkInvalid"));
    }

    private static ReportSheet ReadPhase3WithFeatures(MemoryStream input)
    {
        input.Position = 0;
        using var prepared = OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default).GetAwaiter().GetResult();
        var core = new FullExcelReader(null, null, true).Read(prepared, null);
        return CoreModelAdapter.ToPublic(core.Sheets[0], new Excel.StylePool());
    }

    private static IEnumerable<DrawCommand> Flatten(IEnumerable<DrawCommand> commands) => commands.SelectMany(command => command is DrawViewportCommand viewport ? Flatten(viewport.Commands) : new[] { command });

    private static string NumericRows(params double[] values) => string.Concat(values.Select((value, index) => $"<row r=\"{index + 1}\"><c r=\"A{index + 1}\"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c></row>"));

    private static string Point(string type, double value, bool extension = false, bool gte = true) => extension ? $"<x14:cfvo type=\"{type}\" gte=\"{(gte ? 1 : 0)}\"><xm:f>{value.ToString(CultureInfo.InvariantCulture)}</xm:f></x14:cfvo>" : $"<cfvo type=\"{type}\" val=\"{value.ToString(CultureInfo.InvariantCulture)}\" gte=\"{(gte ? 1 : 0)}\"/>";

    private static string Icons(string points, string attributes = "", int priority = 1, string ranges = "A1:A10000") => Rule("iconSet", $"<iconSet {attributes}>{points}</iconSet>", priority, null, ranges);

    private static string Bar(string points, string attributes = "", bool extension = false, int? priority = 1, string ranges = "A1:A10000", bool linked = false)
    {
        if (!extension)
        {
            return Rule("dataBar", $"<dataBar {attributes}>{points}<color rgb=\"FF0080FF\"/></dataBar>", priority ?? 1, null, ranges);
        }

        return $"<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{{78C0D931-6437-407d-A8EE-F0AAD7539E65}}\"><x14:conditionalFormattings xmlns:x14=\"{X14Namespace}\" xmlns:xm=\"{XmNamespace}\"><x14:conditionalFormatting><x14:cfRule type=\"dataBar\" {(priority is null ? "" : "priority=\"" + priority + "\"")} id=\"{LinkId}\"><x14:dataBar {attributes}>{points}{(linked ? "" : "<x14:fillColor rgb=\"FF0080FF\"/>")}<x14:negativeFillColor rgb=\"FFFF0000\"/><x14:axisColor rgb=\"FF000000\"/></x14:dataBar></x14:cfRule><xm:sqref>{ranges}</xm:sqref></x14:conditionalFormatting></x14:conditionalFormattings></ext></extLst>";
    }

    private static void SavePhase3Artifact(string name, byte[] bytes)
    {
        if (Environment.GetEnvironmentVariable("EXCELRENDERER_CF_PHASE3_ARTIFACTS") is { } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
        }
    }
}
