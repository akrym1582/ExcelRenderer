using ExcelRenderer.SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// PngSampleOutputTests が表すデータと操作を提供します.
/// </summary>
public sealed class PngSampleOutputTests
{
    /// <summary>
    /// Generates_pngs_from_prebuilt_excel を実行します.
    /// </summary>
    /// <param name="excelFileName">excelFileName に渡す値です。</param>
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
