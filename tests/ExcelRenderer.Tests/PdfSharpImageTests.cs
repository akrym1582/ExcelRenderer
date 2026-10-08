using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>PDF 出力で解読不能な画像をスキップし、診断通知と後続の描画を継続することを検証します。</summary>
public sealed class PdfSharpImageTests
{
    /// <summary>不正画像をスキップして診断を通知し、後続の正常画像とページが PDF に保持されることを検証します。</summary>
    /// <param name="hex">解読不能な画像データの 16 進表記。</param>
    [Theory(DisplayName = "不正画像をスキップして診断を通知し、後続の正常画像とページが PDF に保持される")]
    [InlineData("")]
    [InlineData("0102030405")]
    [InlineData("89504E470D0A1A0A")]
    [InlineData("3C73766720786D6C6E733D22687474703A2F2F7777772E77332E6F72672F323030302F737667222F3E")]
    public void Render_skips_undecodable_images_and_reports_details(string hex)
    {
        var diagnostics = new List<ConversionDiagnostic>();
        var renderer = new PdfSharpRenderer { DiagnosticHandler = diagnostics.Add };
        var invalid = new DrawImageCommand(2, new ReportRect(1, 2, 3, 4), Convert.FromHexString(hex));
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = new MemoryStream();

        renderer.Render(
            [
                new FillRectangleCommand(1, new ReportRect(0, 0, 10, 10), new ReportColor(255, 0, 0)),
                new DrawViewportCommand(2, [invalid], new ReportRect(0, 0, 20, 20), 0, 0),
                new DrawImageCommand(2, new ReportRect(5, 5, 10, 10), data.ToArray()),
            ],
            new PageSettings(20, 20),
            output);

        var warning = Assert.Single(diagnostics);
        Assert.Equal("ImageDecodeFailed", warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(DiagnosticStage.Render, warning.Stage);
        Assert.Equal(2, warning.SourcePageNumber);
        Assert.Equal("image@1,2,3,4", warning.ObjectId);
        Assert.Contains($"Bytes={hex.Length / 2}", warning.Message);
        Assert.Contains("header=" + BitConverter.ToString(invalid.ImageBytes, 0, Math.Min(16, invalid.ImageBytes.Length)), warning.Message);
        Assert.Contains(hex.Length == 0 ? "empty" : "SKCodec.Create returned null", warning.Message);
        output.Position = 0;
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
        var resources = pdf.Pages[1].Elements.GetDictionary("/Resources");
        Assert.NotNull(resources);
        Assert.NotNull(resources.Elements.GetDictionary("/XObject"));
    }

    /// <summary>診断コールバックを指定しなくても、不正画像をスキップして PDF の描画を継続することを検証します。</summary>
    [Fact(DisplayName = "診断コールバックを指定しなくても、不正画像をスキップして PDF の描画を継続する")]
    public void Render_skips_invalid_image_without_callback()
    {
        using var output = new MemoryStream();
        new PdfSharpRenderer().Render(
            [new DrawImageCommand(1, new ReportRect(0, 0, 10, 10), [1, 2, 3])],
            new PageSettings(20, 20),
            output);

        output.Position = 0;
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, pdf.PageCount);
    }
}
