using ExcelRenderer.SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PngSampleOutputTests
{
    [Theory]
    [MemberData(nameof(SampleOutputTestSupport.RenderSamples), MemberType = typeof(SampleOutputTestSupport))]
    public void Generates_pngs_from_prebuilt_excel(string excelFileName)
    {
        SampleOutputTestSupport.ConfigureJapaneseFont();
        var sample = SampleOutputTestSupport.ReadAndLayout(excelFileName);
        var outputPaths = new Dictionary<int, string>();

        new PngRenderer().Render(sample.Commands, sample.Sheet.PageSettings, pageNumber =>
        {
            var path = SampleOutputTestSupport.OutputPath(excelFileName, $"-page-{pageNumber}.png");
            outputPaths.Add(pageNumber, path);
            return File.Create(path);
        });

        Assert.Equal(sample.Layout.Pages.Count, outputPaths.Count);
        Assert.All(outputPaths.Values, path =>
        {
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        });
    }
}
