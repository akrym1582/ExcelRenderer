using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class RichTextFallbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyMeasurerFallbackIsOptInAndFailsBeforeOutput(bool detect)
    {
        using var input = RetainedWorkbook.Open("RichTextFallbackTests.LegacyMeasurerFallbackIsOptInAndFailsBeforeOutput.8956CC99987D66F6.xlsx");
        input.Position = 0;
        using var output = new MemoryStream();
        var measurer = new LegacyMeasurer();
        var request = new RenderRequest
        {
            OutputFormat = OutputFormat.Pdf,
            TextMeasurer = measurer,
            DiagnosticOptions = new() { DetectUnsupportedFeatures = detect },
        };
        var result = await ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output));
        Assert.Equal(detect ? 1 : 0, result.Diagnostics.Count(d => d.Code == "RichTextLayoutFallback"));
        Assert.Contains("secret text", measurer.Texts);
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("secret"));
        if (detect)
        {
            var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "RichTextLayoutFallback");
            Assert.Equal("A1", diagnostic.CellRange);
            Assert.Equal(1, diagnostic.SourcePageNumber);
            Assert.Equal(1, diagnostic.OccurrenceCount);
            input.Position = 0;
            using var sentinel = new MemoryStream();
            sentinel.Write([9, 8]);
            await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request with
            {
                DiagnosticOptions = new() { DetectUnsupportedFeatures = true, TreatAsErrors = ["RichTextLayoutFallback"], SuppressedCodes = ["RichTextLayoutFallback"] },
            }, new SingleStreamOutputSink(sentinel)));
            Assert.Equal(new byte[] { 9, 8 }, sentinel.ToArray());
        }
    }

    private sealed class LegacyMeasurer : ITextMeasurer
    {
        internal List<string> Texts { get; } = [];

        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap)
        {
            Texts.Add(text);
            return new(text.Length * 5, 12);
        }
    }
}
