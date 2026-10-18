using ExcelRenderer.Excel;
using ExcelRenderer.Mapping;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class OpenXmlEndToEndTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReaderFiltersDiagnosticsBySelectedPages(int page)
    {
        using var input = RetainedWorkbook.Open("OpenXmlEndToEndTests.AutoKeepsOpenXmlAndFiltersDiagnosticsBySelectedPages.A0268BF491EE7720.xlsx");
        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new RenderRequest
        {
            OutputFormat = OutputFormat.Pdf,
            Selection = new() { Pages = [page] },
            ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.CachedValue },
        }, new SingleStreamOutputSink(output));
        Assert.Single(result.Pages);
        Assert.Equal(page == 2, result.Diagnostics.Any(diagnostic => diagnostic.Code == "RegionalNumberFormat"));
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    [InlineData(OutputFormat.Markdown)]
    public async Task MappedFormulaFreeWorkbookRendersThroughStreamingReader(OutputFormat format)
    {
        using var source = RetainedWorkbook.Open("OpenXmlEndToEndTests.MappedFormulaFreeWorkbookRendersThroughStreamingReader.3D5603D2784D3681.xlsx");
        source.Position = 0;
        using var mapped = new MemoryStream();
        ExcelTemplateMapper.Map(source, mapped, new { items = new[] { new { name = "日本語の商品", price = 1234 } } });
        mapped.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(mapped, new RenderRequest
        {
            OutputFormat = format,
            ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.CachedValue },
        }, new SingleStreamOutputSink(output));
        Assert.Equal("Completed", result.CompletionStatus);
        Assert.NotEmpty(output.ToArray());
        if (format == OutputFormat.Pdf)
        {
            output.Position = 0;
            using var pdf = PdfTestFile.Open(output);
            Assert.Equal(1, pdf.PageCount);
        }
    }

    [Fact]
    public async Task CallerCalculatedMappingValuesReachStreamingMarkdown()
    {
        using var template = RetainedWorkbook.Open("OpenXmlEndToEndTests.CallerCalculatedMappingAutoValuesReachStreamingMarkdown.29D1CBEC155574A1.xlsx");
        var rendered = new List<byte[]>();
        foreach (var amount in new[] { 7, 13 })
        {
            template.Position = 0;
            using var mapped = new MemoryStream();
            ExcelTemplateMapper.Map(template, mapped, new { amount, doubled = amount * 2 });
            mapped.Position = 0;
            using (var reader = OpenXml.Reading.OpenXmlWorkbookReader.Open(mapped))
            {
                Assert.Equal(amount * 2d, reader.EnumerateCells(1).Single(cell => cell.Address.Column == 2).Value.Number);
            }

            mapped.Position = 0;
            using var output = new MemoryStream();
            await ExcelConverter.RenderAsync(mapped, new RenderRequest
            {
                OutputFormat = OutputFormat.Markdown,
                ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.CachedValue },
            }, new SingleStreamOutputSink(output));
            Assert.Contains((amount * 2).ToString(), System.Text.Encoding.UTF8.GetString(output.ToArray()));
            rendered.Add(output.ToArray());
        }

        Assert.False(rendered[0].SequenceEqual(rendered[1]));
    }

    [Fact]
    public async Task SingleFontCoreUsesStreamingReaderWithoutLoadingClosedXml()
    {
        using var input = RetainedWorkbook.Open("OpenXmlEndToEndTests.SingleFontCoreUsesStreamingReaderWithoutLoadingClosedXml.795F3BEBD5ACE77C.xlsx");
        input.Position = 0;
        using var output = new MemoryStream();
        var loads = 0;
        Core.Rendering.ConversionMetrics.Observer = (name, value) => { if (name == "coreReaderWorkbook") loads += (int)value; };
        try
        {
            var result = await Core.ExcelConverter.ConvertAsync(input, output, new()
            {
                FontFilePath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"),
                ReadOptions = new() { FormulaDisplayMode = Core.FormulaDisplayMode.CachedValue },
            });
            Assert.Equal(1, result.PageCount);
            Assert.Equal(0, loads);
        }
        finally
        {
            Core.Rendering.ConversionMetrics.Observer = null;
        }
    }
}
