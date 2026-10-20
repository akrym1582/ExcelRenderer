using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class ImageReviewTests
{
    [Fact]
    public void Complete_header_with_corrupt_pixels_reports_one_warning_and_keeps_later_content()
    {
        var bytes = ImageReviewFixture.CorruptPng();
        using var encoded = SKImage.FromEncodedData(bytes);
        Assert.NotNull(encoded);
        using var codec = SKCodec.Create(new SKMemoryStream(bytes), out var created);
        Assert.NotNull(codec);
        Assert.Equal(SKCodecResult.Success, created);
        using var bitmap = new SKBitmap(codec.Info);
        var status = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        Assert.NotEqual(SKCodecResult.Success, status);
        var evidence = Path.Combine(AppContext.BaseDirectory, "SampleOutputs/ImageReview");
        Directory.CreateDirectory(evidence);
        File.WriteAllBytes(Path.Combine(evidence, "corrupt-idat.png"), bytes);
        File.WriteAllText(Path.Combine(evidence, "decode-status.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Codec = created.ToString(), Pixels = status.ToString(), LazyImageCreated = encoded is not null,
        }));
        var diagnostics = new List<ConversionDiagnostic>();
        using var output = new MemoryStream();
        new PdfRenderer { DiagnosticHandler = diagnostics.Add }.Render(
            [new DrawImageCommand(1, new(0, 0, 10, 10), bytes),
             new DrawImageCommand(1, new(20, 0, 10, 10), ImageReviewFixture.Image()),
             new DrawTextCommand(2, new(0, 0, 100, 20), "AFTER", CellStyle.Default)], new(120, 80), output);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ImageDecodeFailed", diagnostic.Code);
        Assert.Contains("GetPixels", diagnostic.Message);
        output.Position = 0;
        using var pdf = PdfTestFile.Open(output);
        Assert.Equal(2, pdf.PageCount);
        Assert.Contains("AFTER", pdf.Pages[1].Text);
    }

    [Theory]
    [InlineData("warning")]
    [InlineData("strict")]
    [InlineData("code")]
    public async Task Pixel_failure_uses_existing_high_level_failure_and_manifest_policy(string policy)
    {
        using var input = SdkWorkbook.Create(new DocumentFormat.OpenXml.Spreadsheet.Worksheet(
            new DocumentFormat.OpenXml.Spreadsheet.SheetData(new DocumentFormat.OpenXml.Spreadsheet.Row(SdkWorkbook.Text("A1", "AFTER")) { RowIndex = 1 })), SdkWorkbook.Styles());
        using (var package = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single();
            SdkWorkbook.Image(sheet, ImageReviewFixture.CorruptPng());
            SdkWorkbook.Image(sheet, ImageReviewFixture.Image(), row: 2);
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var sink = new ObservedSink(output);
        var request = new RenderRequest
        {
            OutputFormat = OutputFormat.Pdf,
            FontOptions = new() { AllowSystemFonts = false },
            ReadOptions = new() { ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 7 } },
            DiagnosticOptions = new() { StrictMode = policy == "strict", TreatAsErrors = policy == "code" ? ["ImageDecodeFailed"] : [] },
        };
        IReadOnlyList<ConversionDiagnostic> diagnostics;
        if (policy == "warning")
        {
            var result = await ExcelConverter.RenderAsync(input, request, sink);
            diagnostics = result.Diagnostics;
            Assert.Single(result.Artifacts);
            Assert.Equal(1, sink.Completes);
            output.Position = 0;
            using var pdf = PdfTestFile.Open(output);
            Assert.Contains("AFTER", pdf.Pages[0].Text);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request, sink));
            diagnostics = error.CollectedDiagnostics;
            Assert.Empty(error.CompletedArtifacts);
            Assert.Equal(0, sink.Completes);
            Assert.Equal(1, sink.Aborts);
        }

        var warning = Assert.Single(diagnostics, diagnostic => diagnostic.Code == "ImageDecodeFailed");
        Assert.Equal("data", warning.SheetName);
        Assert.Equal(1, warning.SourcePageNumber);
        Assert.Contains("SKCodec.GetPixels returned", warning.Message);
        Assert.Contains("Bytes=", warning.Message);
        Assert.Contains("header=89-50-4E-47", warning.Message);
        Assert.StartsWith("image@", warning.ObjectId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partial_png_and_jpeg_are_not_accepted_as_complete_images(bool jpeg)
    {
        var bytes = ImageReviewFixture.Image(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png);
        bytes = bytes.Take(bytes.Length / 2).ToArray();
        using var codec = SKCodec.Create(new SKMemoryStream(bytes), out var created);
        Assert.NotNull(codec);
        Assert.Equal(SKCodecResult.Success, created);
        using var bitmap = new SKBitmap(codec.Info);
        var status = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        Assert.NotEqual(SKCodecResult.Success, status);
        using var resources = new Core.Rendering.ImageResources();
        Assert.Null(resources.AcquireImage(bytes, out var reason));
        Assert.Contains(status.ToString(), reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_valid_images_decode_once_and_keep_original_jpeg_or_png_encoding(bool jpeg)
    {
        var bytes = ImageReviewFixture.Image(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png);
        var metrics = new Dictionary<string, double>();
        Core.Rendering.ConversionMetrics.Observer = (key, value) => metrics[key] = metrics.GetValueOrDefault(key) + value;
        try
        {
            using var owner = new Core.Rendering.ImageResources();
            using var first = owner.AcquireImage(bytes, out _);
            Assert.NotNull(first);
            for (var index = 0; index < 10; index++)
            {
                using var next = owner.AcquireImage(bytes, out _);
                Assert.Same(first.Value, next!.Value);
            }

            Assert.Equal(1, metrics["imagePixelDecode"]);
            using var encoded = first.Value.SvgImage.EncodedData;
            Assert.Equal(bytes, encoded.ToArray());
            if (jpeg)
            {
                using var pdfEncoded = first.Value.PdfImage.EncodedData;
                Assert.Equal(bytes, pdfEncoded.ToArray());
            }

            metrics.Clear();
            using var output = new MemoryStream();
            new PdfRenderer().Render([new DrawImageCommand(1, new(0, 0, 100, 100), bytes),
                new DrawImageCommand(2, new(0, 0, 100, 100), bytes)
                { Crop = new(.2, .1, .1, .25), Rotation = 7, ClipBounds = new(0, 0, 100, 100) }], new(100, 100), output);
            Assert.Equal(1, metrics["imagePixelDecode"]);
            var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs/ImageReview");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, jpeg ? "jpeg-e2e.pdf" : "png-e2e.pdf"), output.ToArray());
            File.WriteAllBytes(Path.Combine(directory, jpeg ? "original.jpg" : "original.png"), bytes);

        }
        finally
        {
            Core.Rendering.ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Failure_cache_keeps_actual_reason_without_deduplicating_locations()
    {
        var bytes = ImageReviewFixture.CorruptPng();
        var diagnostics = new List<ConversionDiagnostic>();
        using var output = new MemoryStream();
        new PdfRenderer { DiagnosticHandler = diagnostics.Add }.Render(
            [new DrawImageCommand(1, new(0, 0, 10, 10), bytes), new DrawImageCommand(1, new(20, 0, 10, 10), bytes)], new(100, 100), output);
        Assert.Equal(2, diagnostics.Count);
        Assert.NotEqual(diagnostics[0].ObjectId, diagnostics[1].ObjectId);
        Assert.All(diagnostics, diagnostic => Assert.Contains("GetPixels", diagnostic.Message));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pixel_failures_do_not_change_valid_png_svg_transformed_output(bool svg)
    {
        var bytes = ImageReviewFixture.Image(alpha: true);
        var bad = ImageReviewFixture.CorruptPng();
        SKColor[] Render(bool includeInvalid)
        {
            var commands = new List<DrawCommand>();
            if (includeInvalid) commands.Add(new DrawImageCommand(1, new(0, 0, 20, 20), bad));
            commands.Add(new DrawImageCommand(1, new(10, 10, 60, 60), bytes)
            {
                Rotation = 30, FlipHorizontal = true, Crop = new(.1, .2, .1, .1), ClipBounds = new(0, 0, 70, 70),
            });
            using var output = new MemoryStream();
            if (!svg)
            {
                new ExcelRenderer.SkiaSharp.PngRenderer().RenderPage(commands, new(80, 80), output, 72);
                using var bitmap = SKBitmap.Decode(output.ToArray());
                return bitmap.Pixels;
            }

            new ExcelRenderer.SkiaSharp.SvgRenderer().RenderPage(commands, new(80, 80), output);
            output.Position = 0;
            using var renderer = new Svg.Skia.SKSvg();
            renderer.Load(output);
            using var target = new SKBitmap(80, 80);
            using var canvas = new SKCanvas(target);
            canvas.Clear(SKColors.White);
            canvas.DrawPicture(renderer.Picture);
            return target.Pixels;
        }

        Assert.Equal(Render(false), Render(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decoded_image_lifetime_respects_cache_budget_and_active_leases(bool cacheable)
    {
        var owner = new Core.Rendering.ImageResources(cacheable ? 16 * 1024 * 1024 : 1);
        var bytes = ImageReviewFixture.Image(alpha: true, unpremultiplied: true);
        var lease = owner.AcquireImage(bytes, out _)!;
        var images = new[] { lease.Value.Raster, lease.Value.PdfImage, lease.Value.SvgImage };
        owner.Dispose();
        Assert.All(images, image => Assert.NotEqual(IntPtr.Zero, image.Handle));
        lease.Dispose();
        Assert.All(images, image => Assert.Equal(IntPtr.Zero, image.Handle));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Complete_decode_keeps_native_exif_orientation(int origin)
    {
        var jpeg = ImageReviewFixture.Image(SKEncodedImageFormat.Jpeg);
        byte[] exif = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)origin, 0, 0, 0, 0, 0, 0, 0];
        var bytes = jpeg.Take(2).Concat(exif).Concat(jpeg.Skip(2)).ToArray();
        using var reference = SKImage.FromEncodedData(bytes);
        using var expected = SKBitmap.FromImage(reference);
        using var actual = Core.Rendering.ImageDecoder.Decode(bytes, out var reason);
        Assert.Null(reason);
        Assert.NotNull(actual);
        using var pixels = SKBitmap.FromImage(actual.Raster);
        Assert.Equal(expected.Pixels, pixels.Pixels);
    }

    [Fact]
    public void Complete_decode_matches_native_transparent_pixels_without_an_extra_roundtrip()
    {
        var bytes = ImageReviewFixture.Image(alpha: true, unpremultiplied: true);
        using var native = SKImage.FromEncodedData(bytes);
        using var decoded = Core.Rendering.ImageDecoder.Decode(bytes, out var reason);
        Assert.Null(reason);
        var info = native.Info.WithAlphaType(SKAlphaType.Unpremul);
        using var expected = new SKBitmap(info);
        using var actual = new SKBitmap(info);
        Assert.True(native.ReadPixels(info, expected.GetPixels(), expected.RowBytes, 0, 0));
        Assert.True(decoded!.PdfImage.ReadPixels(info, actual.GetPixels(), actual.RowBytes, 0, 0));
        Assert.Equal(expected.GetPixelSpan().ToArray(), actual.GetPixelSpan().ToArray());
        using var raster = SKBitmap.FromImage(decoded.Raster);
        using var nativeRaster = SKBitmap.FromImage(native);
        Assert.Equal(nativeRaster.Pixels, raster.Pixels);
    }

    private sealed class ObservedSink(Stream stream) : IRenderOutputSink
    {
        public int Completes { get; private set; }
        public int Aborts { get; private set; }
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(stream);
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) { Completes++; return default; }
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) { Aborts++; return default; }
    }
}
