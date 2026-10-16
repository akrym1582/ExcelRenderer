using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class OpenXmlIntegrationTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Directory.EnumerateFiles(Path.Combine(root, "samples"), "*.xlsx", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests/ExcelRenderer.Tests/SampleInputs"), "*.xlsx", SearchOption.AllDirectories))
            .Select(path => new object[] { path });
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExistingFixturesPreserveRenderedCellSnapshots(string path)
    {
        var expected = new ExcelReader().Read(path, new() { FormulaDisplayMode = FormulaDisplayMode.CachedValue });
        var workbooks = 0;
        Core.Rendering.ConversionMetrics.Observer = (name, value) => { if (name == "coreReaderWorkbook") workbooks += (int)value; };
        ReportDocument actual;
        try
        {
            actual = new ExcelReader().Read(path, new() { Backend = ReaderBackend.OpenXml, FormulaDisplayMode = FormulaDisplayMode.CachedValue });
        }
        finally
        {
            Core.Rendering.ConversionMetrics.Observer = null;
        }

        Assert.Equal(0, workbooks);
        Assert.Equal(expected.Sheets.Count, actual.Sheets.Count);
        for (var index = 0; index < expected.Sheets.Count; index++)
        {
            var old = expected.Sheets[index];
            var sheet = actual.Sheets[index];
            Assert.Equal(old.Name, sheet.Name);
            foreach (var pair in old.Cells)
            {
                Assert.True(sheet.Cells.TryGetValue(pair.Key, out var cell), path + " " + pair.Key);
                Assert.True((pair.Value.Text ?? string.Empty) == (cell!.Text ?? string.Empty), path + " " + pair.Key + " text: " + pair.Value.Text + " / " + cell.Text);
                Assert.True(pair.Value.Style == cell.Style, path + " " + pair.Key + " style: " + pair.Value.Style + " / " + cell.Style);
                Assert.Equal(pair.Value.RowSpan, cell.RowSpan);
                Assert.Equal(pair.Value.ColumnSpan, cell.ColumnSpan);
                Assert.Equal(pair.Value.Formula, cell.Formula);
                Assert.Equal(pair.Value.MergedBorders, cell.MergedBorders);
            }

            Assert.Equal(old.MergedRanges, sheet.MergedRanges);
            Assert.Equal(old.PrintAreas, sheet.PrintAreas);
            Assert.Equal(old.DefaultColumnWidth, sheet.DefaultColumnWidth, 6);
            Assert.Equal(old.DefaultRowHeight, sheet.DefaultRowHeight, 6);
            Assert.Equal(old.PageSettings with { ManualRowBreaks = [], ManualColumnBreaks = [] }, sheet.PageSettings with { ManualRowBreaks = [], ManualColumnBreaks = [] });
            Assert.Equal(old.PageSettings.ManualRowBreaks, sheet.PageSettings.ManualRowBreaks);
            Assert.Equal(old.PageSettings.ManualColumnBreaks, sheet.PageSettings.ManualColumnBreaks);
            Assert.Equal(old.Images?.Count ?? 0, sheet.Images?.Count ?? 0);
            for (var imageIndex = 0; imageIndex < (old.Images?.Count ?? 0); imageIndex++)
            {
                var expectedImage = old.Images![imageIndex];
                var actualImage = sheet.Images![imageIndex];
                Assert.Equal(expectedImage.Width, actualImage.Width);
                Assert.Equal(expectedImage.Height, actualImage.Height);
                Assert.Equal(expectedImage.OffsetX, actualImage.OffsetX);
                Assert.Equal(expectedImage.OffsetY, actualImage.OffsetY);
                Assert.Equal(expectedImage.Anchor, actualImage.Anchor);
                Assert.Equal(expectedImage.ImageBytes, actualImage.ImageBytes);
            }

            foreach (var column in old.Columns)
            {
                Assert.Equal(column.Value, sheet.Columns.GetValueOrDefault(column.Key, new(sheet.DefaultColumnWidth)));
            }

            foreach (var row in old.Rows)
            {
                Assert.Equal(row.Value, sheet.Rows.GetValueOrDefault(row.Key, new(sheet.DefaultRowHeight)));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ExistingFixturesRenderIdenticalPngSvgAndMarkdown(string path)
    {
        foreach (var format in new[] { OutputFormat.Png, OutputFormat.Svg, OutputFormat.Markdown })
        {
            using var old = new ArtifactCapture();
            using var actual = new ArtifactCapture();
            foreach (var entry in new[] { (ReaderBackend.ClosedXml, old), (ReaderBackend.OpenXml, actual) })
            {
                using var input = File.OpenRead(path);
                await ExcelConverter.RenderAsync(input, new RenderRequest
                {
                    OutputFormat = format,
                    ReadOptions = new() { Backend = entry.Item1, FormulaDisplayMode = FormulaDisplayMode.CachedValue },
                }, entry.Item2);
            }

            Assert.Equal(old.Artifacts.Keys.Order(), actual.Artifacts.Keys.Order());
            foreach (var artifact in old.Artifacts)
            {
                // Native Skia clip/image resource IDs are process-global; normalize declarations and exact references only.
                var expectedBytes = artifact.Value.ToArray();
                var actualBytes = actual.Artifacts[artifact.Key].ToArray();
                var equal = format == OutputFormat.Svg ? CanonicalSvg(expectedBytes) == CanonicalSvg(actualBytes) : expectedBytes.SequenceEqual(actualBytes);
                if (!equal)
                {
                    var directory = Path.Combine(Path.GetTempPath(), "excelrenderer-backend-diffs");
                    Directory.CreateDirectory(directory);
                    var name = Path.GetFileNameWithoutExtension(path) + "-" + artifact.Key;
                    File.WriteAllBytes(Path.Combine(directory, "closedxml-" + name), artifact.Value.ToArray());
                    File.WriteAllBytes(Path.Combine(directory, "openxml-" + name), actual.Artifacts[artifact.Key].ToArray());
                }

                Assert.True(equal, path + " " + format + " " + artifact.Key);
            }
        }
    }

    [Fact]
    public void PaintedRowAndColumnDefaultsUseFinitePrintBoundsAndCellOverrides()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("paint");
        sheet.Cell("A1").Value = "header";
        sheet.Cell("C3").Value = "footer";
        sheet.Row(2).Style.Fill.BackgroundColor = XLColor.Yellow;
        sheet.Column(2).Style.Fill.BackgroundColor = XLColor.Red;
        sheet.Cell("B2").Style = workbook.Style;
        sheet.PageSetup.PrintAreas.Add("A1:C3");
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        var actual = new ExcelReader().Read(input, new() { Backend = ReaderBackend.OpenXml, FormulaDisplayMode = FormulaDisplayMode.CachedValue }).Sheets.Single();
        Assert.Equal(new ReportColor(255, 255, 0), actual.Cells[new(2, 1)].Style.Background);
        Assert.Equal(new ReportColor(255, 0, 0), actual.Cells[new(1, 2)].Style.Background);
        Assert.Null(actual.Cells[new(2, 2)].Style.Background);
        Assert.InRange(actual.Cells.Count, 5, 9);
    }

    [Fact]
    public void ExplicitReaderPreservesValuesStylesMergesAndPrintGeometry()
    {
        using var workbook = new XLWorkbook();
        var source = workbook.AddWorksheet("invoice");
        source.Cell("A1").Value = "商品";
        source.Cell("B2").Value = 12.5;
        source.Cell("B2").Style.NumberFormat.Format = "0.00";
        source.Cell("B2").Style.Font.SetBold().Font.SetFontColor(XLColor.Red);
        source.Cell("B2").Style.Fill.SetBackgroundColor(XLColor.Yellow);
        source.Cell("D4").Style.Border.SetRightBorder(XLBorderStyleValues.Double);
        source.Range("C3:D4").Merge();
        source.Column(2).Width = 17;
        source.Row(2).Height = 30;
        source.PageSetup.PrintAreas.Add("A1:F8");
        source.PageSetup.SetRowsToRepeatAtTop(1, 1);
        source.PageSetup.AddHorizontalPageBreak(4);
        source.PageSetup.SetScale(80);
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        var old = new ExcelReader().Read(input, new() { FormulaDisplayMode = FormulaDisplayMode.CachedValue }).Sheets.Single();
        input.Position = 0;
        ReaderBackendSelection? selection = null;
        var actual = new ExcelReader().Read(input, new() { Backend = ReaderBackend.OpenXml, FormulaDisplayMode = FormulaDisplayMode.CachedValue, BackendSelected = value => selection = value }).Sheets.Single();
        Assert.Equal(ReaderBackend.OpenXml, selection!.SelectedBackend);
        foreach (var pair in old.Cells)
        {
            Assert.True(actual.Cells.TryGetValue(pair.Key, out var cell), pair.Key.ToString());
            Assert.Equal(pair.Value.Text ?? string.Empty, cell!.Text ?? string.Empty);
            Assert.Equal(pair.Value.Style, cell.Style);
            Assert.Equal(pair.Value.RowSpan, cell.RowSpan);
            Assert.Equal(pair.Value.ColumnSpan, cell.ColumnSpan);
            Assert.Equal(pair.Value.MergedBorders, cell.MergedBorders);
        }

        Assert.Equal(old.Columns[2], actual.Columns[2]);
        Assert.Equal(old.Rows[2], actual.Rows[2]);
        Assert.Equal(old.PageSettings with { ManualRowBreaks = [], ManualColumnBreaks = [] }, actual.PageSettings with { ManualRowBreaks = [], ManualColumnBreaks = [] });
        Assert.Equal(old.PageSettings.ManualRowBreaks, actual.PageSettings.ManualRowBreaks);
        Assert.Equal(old.PageSettings.ManualColumnBreaks, actual.PageSettings.ManualColumnBreaks);
        Assert.Equal(old.PrintAreas, actual.PrintAreas);
        Assert.True(input.CanRead);
    }

    [Fact]
    public void DefaultAndAutoLegacyRetainCompatibilityEngine()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("data").Cell("A1").Value = "hello";
        using var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        ReaderBackendSelection? selected = null;
        new ExcelReader().Read(input, new() { Backend = ReaderBackend.Auto, BackendSelected = value => selected = value });
        Assert.Equal(ReaderBackend.ClosedXml, selected!.SelectedBackend);
        Assert.Contains("LegacyFormulaMode", selected.ReasonCodes);
        Assert.Throws<NotSupportedException>(() => new ExcelReader().Read(input, new() { Backend = ReaderBackend.OpenXml }));
    }
    private static string CanonicalSvg(byte[] bytes)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        var ids = document.Root!.DescendantsAndSelf().Attributes("id").Select((attribute, index) => (attribute.Value, Name: "resource" + index))
            .ToDictionary(item => item.Value, item => item.Name, StringComparer.Ordinal);
        foreach (var attribute in document.Root.DescendantsAndSelf().Attributes())
        {
            if (attribute.Name.LocalName == "id")
            {
                attribute.Value = ids[attribute.Value];
            }
            else if (attribute.Value.StartsWith("url(#", StringComparison.Ordinal) && attribute.Value.EndsWith(")", StringComparison.Ordinal) && ids.TryGetValue(attribute.Value.Substring(5, attribute.Value.Length - 6), out var referenced))
            {
                attribute.Value = "url(#" + referenced + ")";
            }
            else if (attribute.Value.StartsWith("#", StringComparison.Ordinal) && ids.TryGetValue(attribute.Value.Substring(1), out referenced))
            {
                attribute.Value = "#" + referenced;
            }
        }

        return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private sealed class ArtifactCapture : IRenderOutputSink, IDisposable
    {
        internal Dictionary<string, MemoryStream> Artifacts { get; } = new();
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
        {
            var stream = new MemoryStream();
            Artifacts.Add(artifact.RelativeName, stream);
            return new(stream);
        }

        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
        public void Dispose() { foreach (var stream in Artifacts.Values) stream.Dispose(); }
    }
}
