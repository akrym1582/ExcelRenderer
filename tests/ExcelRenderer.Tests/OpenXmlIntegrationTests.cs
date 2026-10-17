using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
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
        using var culture = new ReaderSnapshot.InvariantCultureScope();
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var name = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        var key = ReaderSnapshot.Hash(Encoding.UTF8.GetBytes(name));
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ReaderGolden", key + ".json"));
        var actual = ReaderSnapshot.Model(new ExcelReader().Read(path));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), name + " model differs from parent snapshot");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ExistingFixturesRenderIdenticalPngSvgAndMarkdown(string path)
    {
        using var culture = new ReaderSnapshot.InvariantCultureScope();
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var name = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ReaderGolden", "artifacts.json")))!;
        foreach (var format in new[] { OutputFormat.Png, OutputFormat.Svg, OutputFormat.Markdown })
        {
            using var input = File.OpenRead(path);
            using var actual = new ReaderSnapshot.Capture();
            await ExcelConverter.RenderAsync(input, new()
            {
                OutputFormat = format,
                FontOptions = new() { AllowSystemFonts = false, UseFontPack = false, FontFiles = [root + "/third_party/NotoSansJP/NotoSansJP-Regular.ttf"] },
            }, actual);
            var hashes = actual.Artifacts.ToDictionary(item => format + "/" + item.Key, item => ReaderSnapshot.ArtifactHash(format, item.Value.ToArray()));
            var expected = all[name].Where(pair => pair.Key.StartsWith(format + "/", StringComparison.Ordinal)).ToDictionary();
            Assert.Equal(expected.Keys.Order(), hashes.Keys.Order());
            foreach (var pair in expected) Assert.True(pair.Value == hashes[pair.Key], name + " " + pair.Key + " differs from parent output");
        }
    }

    [Fact]
    public void PaintedRowAndColumnDefaultsUseFinitePrintBoundsAndCellOverrides()
    {
        using var input = RetainedWorkbook.Open("OpenXmlIntegrationTests.PaintedRowAndColumnDefaultsUseFinitePrintBoundsAndCellOverrides.41F43953BE61398E.xlsx");
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
        using var input = RetainedWorkbook.Open("OpenXmlIntegrationTests.ExplicitReaderPreservesValuesStylesMergesAndPrintGeometry.472BF0D35AAF49AE.xlsx");
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
    public void DefaultAndAutoUseCachedOpenXml()
    {
        using var input = SdkWorkbook.Create(new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData(new DocumentFormat.OpenXml.Spreadsheet.Row(SdkWorkbook.Text("A1", "hello")) { RowIndex = 1 })));
        input.Position = 0;
        ReaderBackendSelection? selected = null;
        new ExcelReader().Read(input, new() { Backend = ReaderBackend.Auto, BackendSelected = value => selected = value });
        Assert.Equal(ReaderBackend.OpenXml, selected!.SelectedBackend);
        Assert.Equal(FormulaDisplayMode.CachedValue, new ExcelReadOptions().FormulaDisplayMode);
        Assert.Equal(ReaderBackend.OpenXml, new ExcelReadOptions().Backend);
        input.Position = 0;
        Assert.Equal("hello", new ExcelReader().Read(input, new() { Backend = ReaderBackend.OpenXml }).Sheets[0].Cells[new(1, 1)].Text);
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
