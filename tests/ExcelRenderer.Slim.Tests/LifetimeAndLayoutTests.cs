using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Drawing;
using ExcelRenderer.Slim.Fonts;
using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;
using ExcelRenderer.Slim.Pdf;
using ExcelRenderer.Slim.Rendering;
using PdfSharp.Fonts;
using Xunit;

namespace ExcelRenderer.Slim.Tests;

public sealed class LifetimeAndLayoutTests
{
    [Fact]
    public async Task Waiting_for_font_gate_is_cancellable()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        ConversionMetrics.Observer = (key, _) => { if (key == "fontSnapshotRead") { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(15))); } };
        var first = Task.Run(() => TestSupport.Convert());
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            using var input = TestSupport.Workbook(); using var output = new MemoryStream(); using var cancellation = new CancellationTokenSource();
            var waiting = ExcelConverter.ConvertAsync(input, output, TestSupport.Options, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(0, output.Length);
        }
        finally { release.Set(); ConversionMetrics.Observer = null; }
        Assert.Equal(1, (await first).Result.PageCount);
        Assert.Equal(1, (await TestSupport.Convert()).Result.PageCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_at_page_or_Save_releases_resources(bool duringSave)
    {
        using var cancellation = new CancellationTokenSource();
        ConversionMetrics.Observer = (key, _) => { if (!duringSave && key == "pagePayloadActive") cancellation.Cancel(); };
        try
        {
            using var input = TestSupport.Workbook(); using var output = new MemoryStream();
            using var target = new TestStream(output, onWrite: () => { if (duringSave) cancellation.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.ConvertAsync(input, target, TestSupport.Options, cancellation.Token));
            if (duringSave) Assert.True(output.Length > 0);
        }
        finally { ConversionMetrics.Observer = null; }
        Assert.Equal(1, (await TestSupport.Convert()).Result.PageCount);
    }

    [Fact]
    public void Headers_use_fixed_timestamp_page_fields_and_first_even_rules()
    {
        var sheet = new ReportSheet("Sheet", new Dictionary<CellAddress, ReportCell>(), new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(200, 200),
            HeaderFooter: new(new(Center: "&A &P/&N &D &T"), new(), new(Center: "first"), null, new(Center: "even")));
        var timestamp = new DateTime(2026, 10, 8, 12, 34, 0);
        Assert.Equal("first", Assert.Single(HeaderFooterLayout.Create(sheet, 1, 3, timestamp)).Text);
        Assert.Equal("even", Assert.Single(HeaderFooterLayout.Create(sheet, 2, 3, timestamp)).Text);
        var header = Assert.Single(HeaderFooterLayout.Create(sheet, 3, 3, timestamp));
        Assert.Equal($"Sheet 3/3 {timestamp.ToShortDateString()} {timestamp.ToShortTimeString()}", header.Text);
        Assert.Equal(SingleFontContext.Family, header.Style.Font.Family);
    }

    [Fact]
    public void Hidden_dimensions_and_origin_offsets_remain_in_geometry()
    {
        var sheet = new ReportSheet("Sheet", new Dictionary<CellAddress, ReportCell> { [new(3, 3)] = new("a", CellStyle.Default) },
            new Dictionary<int, ColumnDefinition> { [1] = new(20), [2] = new(30, true), [3] = new(40) },
            new Dictionary<int, RowDefinition> { [1] = new(10), [2] = new(15, true), [3] = new(25) }, [], new(100, 100), new(new(3, 3), new(3, 3)));
        var geometry = new SheetGeometry(sheet);
        TestSupport.Near(20, geometry.ColumnStart(3)); TestSupport.Near(10, geometry.RowStart(3));
    }

    [Fact]
    public void Image_cache_limit_leases_and_disposal_are_respected()
    {
        var bytes = new byte[] { 1 }; var first = new Resource(); var second = new Resource();
        using (var cache = new ImageResources(10))
        {
            using var lease = cache.Acquire(bytes, () => (first, 8L));
            using (var hit = cache.Acquire<Resource>(bytes, () => throw new InvalidOperationException())) Assert.Same(first, hit!.Value);
            using (var uncached = cache.Acquire(new byte[] { 2 }, () => (second, 8L))) Assert.False(first.Disposed);
            Assert.True(second.Disposed);
        }
        Assert.True(first.Disposed);
    }

    [Fact]
    public async Task HYPERLINK_formula_uses_cached_display_text()
    {
        using var input = TestSupport.Workbook();
        using (var package = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
        {
            var cell = package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().First();
            cell.RemoveAllChildren();
            cell.DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String;
            cell.CellFormula = new("HYPERLINK(\"https://example.com\",\"label\")");
            cell.CellValue = new("label");
            package.WorkbookPart.WorksheetParts.Single().Worksheet.Save();
        }
        input.Position = 0;
        await TestSupport.WithSheet(input, (sheet, _) => Assert.Equal("label", sheet.Cells[new(1, 1)].Text));
    }

    [Fact]
    public void Wrap_and_explicit_newlines_use_single_text_lines()
    {
        using var context = new SingleFontContext(TestSupport.JapaneseFont);
        GlobalFontSettings.ResetFontManagement(); GlobalFontSettings.FontResolver = context.Resolver;
        try
        {
            var measurer = new PdfSharpTextMeasurer(context);
            var layout = measurer.Layout("ab\ncd", new(Size: 10), 100, true);
            Assert.Equal(2, layout.Lines.Count); Assert.True(layout.Lines[0].ExplicitBreak);
            Assert.Equal("ab", layout.Lines[0].Text); Assert.Equal("cd", layout.Lines[1].Text);
            var wrapped = measurer.Layout("日本語ABCDE", new(Size: 10), 15, true);
            Assert.True(wrapped.Lines.Count > 2);
            var cached = measurer.Layout("ab\ncd", new(Size: 10), 100, true); Assert.Same(layout, cached);
            for (var i = 0; i < 520; i++) measurer.Layout(i.ToString(), new(), 100, false);
            Assert.NotSame(layout, measurer.Layout("ab\ncd", new(Size: 10), 100, true));
        }
        finally { GlobalFontSettings.ResetFontManagement(); }
    }

    private sealed class Resource : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
