using ExcelRenderer.PdfSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// サンプル Excel ブックから生成する PDF 出力を検証します。
/// </summary>
public sealed class PdfSampleOutputTests
{
    /// <summary>
    /// サンプル Excel ブックをレイアウトし、内容を含む PDF を生成できることを検証します。
    /// </summary>
    /// <param name="excelFileName">入力に使用するサンプル Excel ファイルの名前。</param>
    [Theory]
    [MemberData(nameof(SampleOutputTestSupport.RenderSamples), MemberType = typeof(SampleOutputTestSupport))]
    public void Generates_pdf_from_prebuilt_excel(string excelFileName)
    {
        SampleOutputTestSupport.ConfigureJapaneseFont();
        var sample = SampleOutputTestSupport.ReadAndLayout(excelFileName);
        var outputPath = SampleOutputTestSupport.OutputPath(excelFileName, ".pdf");

        using (var output = File.Create(outputPath))
        {
            new PdfSharpRenderer().Render(sample.Commands, sample.Sheet.PageSettings, output);
        }

        var bytes = File.ReadAllBytes(outputPath);
        Assert.True(bytes.Length > 0, $"PDF が出力されていません: {outputPath}");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }
}
