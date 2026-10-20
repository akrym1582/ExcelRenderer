using System.Security.Cryptography;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Pdf.Structure;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class ImagePayloadTests
{
    [Theory]
    [InlineData("rgb", false, false, 1, false)]
    [InlineData("alpha", false, true, 1, false)]
    [InlineData("jpeg", true, false, 1, false)]
    [InlineData("shared", false, false, 3, false)]
    [InlineData("multiple", false, true, 2, true)]
    public void Finalization_stores_large_encoded_image_payload_once(string name, bool jpeg, bool alpha, int pages, bool distinct)
    {
        var bytes = TestImages.Image(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, alpha);
        using var image = SKImage.FromEncodedData(bytes);
        using var other = distinct ? SKImage.FromEncodedData(TestImages.Image(alpha: true, seed: 23)) : null;
        using var raw = new MemoryStream();
        using (var document = SKDocument.CreatePdf(raw))
        {
            for (var page = 0; page < pages; page++)
            {
                var canvas = document.BeginPage(100, 100);
                canvas.Save();
                if (name != "rgb")
                {
                    canvas.ClipRect(new(2, 3, 98, 97));
                    canvas.RotateDegrees(7, 50, 50);
                }
                canvas.DrawImage(page == 1 && distinct ? other! : image, new SKRect(0, 0, 100, 100), new SKSamplingOptions(SKFilterMode.Linear));
                canvas.Restore();
                document.EndPage();
            }

            document.Close();
        }

        using var final = new MemoryStream();
        var corrections = Enumerable.Range(0, pages).Select(_ => new GeneratedPdfFinalizer.PageCorrection(100, 100, [])).ToArray();
        GeneratedPdfFinalizer.Write(raw, final, corrections, default);
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs/ImageReview");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + "-raw.pdf"), raw.ToArray());
        File.WriteAllBytes(Path.Combine(directory, name + "-final.pdf"), final.ToArray());
        Assert.True(final.Length <= raw.Length + 65536, $"raw={raw.Length}; final={final.Length}");
        var original = new GeneratedPdfFile(raw);
        var updated = new GeneratedPdfFile(final);
        Assert.Null(updated.Trailer.Get("/Prev"));
        var imageObjects = 0;
        foreach (var reference in original.References)
        {
            var before = original.Read(reference);
            var after = updated.Read(reference);
            if (before.StreamOffset == 0) continue;
            using var left = new MemoryStream();
            using var right = new MemoryStream();
            original.CopyStream(before, left, default);
            updated.CopyStream(after, right, default);
            Assert.Equal(SHA256.HashData(left.ToArray()), SHA256.HashData(right.ToArray()));
            if (before.Value is PdfMap map && map.Get("/Subtype") is PdfName { Value: "/Image" })
            {
                imageObjects++;
                var payload = left.ToArray();
                Assert.Equal(1, Occurrences(final.ToArray(), payload));
                if (jpeg) Assert.Equal(bytes, payload);
            }
        }

        Assert.Equal(distinct ? 4 : alpha ? 2 : 1, imageObjects);
        using var independent = TestSupport.Open(final.ToArray());
        Assert.Equal(pages, independent.NumberOfPages);
        using var again = new MemoryStream();
        GeneratedPdfFinalizer.Write(final, again, corrections, default);
        Assert.Equal(final.ToArray(), again.ToArray());
    }

    [Fact]
    public async Task Core_reports_pixel_corruption_once_and_keeps_text_and_valid_image()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var sheet = workbook.WorksheetParts.Single();
            TestWorkbook.Image(sheet, TestImages.CorruptPng(), row: 0);
            TestWorkbook.Image(sheet, TestSupport.Png(), row: 2);
        });
        using var output = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        var warning = Assert.Single(result.Diagnostics, value => value.Code == "ImageDecodeFailed");
        Assert.Contains("GetPixels", warning.Message);
        Assert.Equal(1, warning.PageNumber);
        using var pdf = TestSupport.Open(output.ToArray());
        Assert.Contains("abc 123", pdf.GetPage(1).Text);
        var parsed = new GeneratedPdfFile(new MemoryStream(output.ToArray()));
        Assert.Single(parsed.References.Select(parsed.Read), record => record.Value is PdfMap map && map.Get("/Subtype") is PdfName { Value: "/Image" });
    }

    [Theory]
    [InlineData("success")]
    [InlineData("write")]
    [InlineData("cancel")]
    public void Spilled_document_cleans_up_on_completion_write_failure_and_cancellation(string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "image-review-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        using var cancellation = new CancellationTokenSource();
        using var target = new MemoryStream();
        using var output = new TestStream(target, seekable: false, onWrite: () =>
        {
            if (mode == "write") throw new IOException("Injected output failure.");
        });
        try
        {
            using (var document = new SkiaPdfDocument(new() { MemoryThresholdBytes = 1024, AllowTemporaryFiles = true, TemporaryDirectory = directory }, cancellation.Token))
            {
                document.BeforeFinalize = _ =>
                {
                    Assert.NotEmpty(Directory.GetFiles(directory));
                    if (mode == "cancel") cancellation.Cancel();
                };
                using var image = SKImage.FromEncodedData(TestImages.Image());
                document.Prepare();
                document.Append(new(100, 100, []), canvas => canvas.DrawImage(image, new SKRect(0, 0, 100, 100), new SKSamplingOptions(SKFilterMode.Linear)));
                if (mode == "write") Assert.Throws<IOException>(() => document.Complete(output));
                else if (mode == "cancel") Assert.Throws<OperationCanceledException>(() => document.Complete(output));
                else document.Complete(output);
            }

            Assert.False(output.Disposed);
            Assert.Empty(Directory.GetFiles(directory));
            if (mode == "success") Assert.True(target.Length > 1000000);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static int Occurrences(byte[] bytes, byte[] payload)
    {
        var result = 0;
        var offset = 0;
        while (offset <= bytes.Length - payload.Length)
        {
            var index = bytes.AsSpan(offset).IndexOf(payload);
            if (index < 0) break;
            result++;
            offset += index + payload.Length;
        }

        return result;
    }
}
