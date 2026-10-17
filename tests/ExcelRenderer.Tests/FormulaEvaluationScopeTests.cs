using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>The product rejects legacy requests before reading input or opening output.</summary>
public sealed class FormulaEvaluationScopeTests
{
    [Theory]
    [InlineData(FormulaDisplayMode.Legacy)]
    public async Task InvalidCalculationModesFailBeforeInputAndSink(FormulaDisplayMode mode)
    {
        var options = new ExcelReadOptions { FormulaDisplayMode = mode };
        Assert.Throws<NotSupportedException>(() => new ExcelReader().Read("missing.xlsx", options));
        foreach (var format in Enum.GetValues<OutputFormat>())
        {
            using var input = new MemoryStream([1, 2, 3]);
            using var output = new MemoryStream();
            await Assert.ThrowsAsync<NotSupportedException>(() => ExcelConverter.RenderAsync(input,
                new() { OutputFormat = format, ReadOptions = options }, new SingleStreamOutputSink(output)));
            Assert.Equal(0, input.Position);
            Assert.Empty(output.ToArray());
        }

        await Assert.ThrowsAsync<NotSupportedException>(() => ExcelConverter.ConvertToPdfAsync("missing.xlsx", "unused.pdf", new() { ReadOptions = options }));
        await Assert.ThrowsAsync<NotSupportedException>(() => ExcelConverter.ConvertToImagesAsync("missing.xlsx", "unused-images", new() { ReadOptions = options }));
        await Assert.ThrowsAsync<NotSupportedException>(() => ExcelConverter.ConvertToSvgAsync("missing.xlsx", "unused-svg", new() { ReadOptions = options }));
        await Assert.ThrowsAsync<NotSupportedException>(() => ExcelConverter.ConvertToMarkdownAsync("missing.xlsx", "unused.md", new() { ReadOptions = options }));

        using var coreInput = new MemoryStream([1, 2, 3]);
        using var coreOutput = new MemoryStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => Core.ExcelConverter.ConvertAsync(coreInput, coreOutput, new()
        {
            FontFilePath = "not-opened.ttf",
            ReadOptions = new() { FormulaDisplayMode = (Core.FormulaDisplayMode)mode },
        }));
        Assert.Equal(0, coreInput.Position);
        Assert.Empty(coreOutput.ToArray());
    }

    [Fact]
    public void UnknownEnumsAreRejectedBeforeUnsupportedModePolicy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExcelReader().Read("missing.xlsx", new()
        {
            FormulaDisplayMode = (FormulaDisplayMode)93,
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Core.ExcelReadOptions
        {
            FormulaDisplayMode = (Core.FormulaDisplayMode)93,
        }.Validate());
        Assert.Equal(Core.FormulaDisplayMode.CachedValue, new Core.ExcelReadOptions().FormulaDisplayMode);
    }
}
