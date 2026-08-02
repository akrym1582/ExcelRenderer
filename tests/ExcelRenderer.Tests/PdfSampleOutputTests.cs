using ExcelRenderer.PdfSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PdfSampleOutputTests
{
    [Theory]
    [MemberData(nameof(SampleOutputTestSupport.RenderSamples), MemberType = typeof(SampleOutputTestSupport))]
    public void Generates_pdf_from_prebuilt_excel(string excelFileName)
    {
        SampleOutputTestSupport.ConfigureJapaneseFont();
        var sample = SampleOutputTestSupport.ReadAndLayout(excelFileName);
        var outputPath = SampleOutputTestSupport.OutputPath(excelFileName, ".pdf");

        using (var output = File.Create(outputPath))
            new PdfSharpRenderer().Render(sample.Commands, sample.Sheet.PageSettings, output);

        var bytes = File.ReadAllBytes(outputPath);
        Assert.True(bytes.Length > 0, $"PDF が出力されていません: {outputPath}");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }
}
