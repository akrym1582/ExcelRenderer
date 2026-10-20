using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.PostScript;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class PostScriptTests
{
    internal static FontManager Manager() => new(new() { AllowSystemFonts = false });

    internal static DrawTextCommand Text(string text, int page = 1, double y = 10) =>
        new(page, new(10, y, 570, 400), text, CellStyle.Default with { Font = new("Noto Sans JP", 16), VerticalAlignment = VerticalAlignment.Top });

    internal static byte[] Render(params DrawCommand[] commands)
    {
        using var output = new MemoryStream();
        new PostScriptRenderer(Manager()).Render(commands, new(595.28, 841.89), output);
        Assert.True(output.CanWrite);
        return output.ToArray();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_stroke_widths_fail_without_committing_output(double width)
    {
        foreach (var border in new[] { false, true })
        {
            var side = new BorderSide(width, LineStyle: BorderLineStyle.Dashed);
            DrawCommand command = border ? new DrawBorderCommand(1, new(10, 10, 20, 20), new(side, side, side, side)) : new DrawLineCommand(1, 10, 10, 30, 10, side);
            using var output = new MemoryStream();
            Assert.Throws<ArgumentOutOfRangeException>(() => new PostScriptRenderer(Manager()).Render([command], new(100, 100), output));
            Assert.Equal(0, output.Length);
        }
    }

    [Fact]
    public void Direct_output_is_deterministic_ascii_with_text_operators_and_embedded_sfnt()
    {
        var commands = new[] { Text("ABC (\\) 日本語 辻\U000E0102\U0002000B\n% quit showpage") };
        var first = Render(commands);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(first, Render(commands));
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Assert.All(first, value => Assert.InRange(value, (byte)0, (byte)127));
        var ps = Encoding.ASCII.GetString(first);
        Assert.StartsWith("%!PS-Adobe-3.0\n", ps);
        Assert.Contains("/FontType 42", ps);
        Assert.Contains("/sfnts [", ps);
        Assert.Contains("> show", ps);
        Assert.DoesNotContain("charpath", ps);
        Assert.DoesNotContain("% quit showpage", ps);
        Assert.EndsWith("%%EOF\n", ps);
        Assert.Contains("%%Pages: 1\n", ps);
        var records = Metadata(ps).ToArray();
        Assert.Contains(records, record => record.Kind == 0 && record.Text == commands[0].Text);
        Assert.Contains(records, record => record.Kind == 1 && record.Text.Contains("\U000E0102", StringComparison.Ordinal));
        ValidateEmbeddedFonts(ps);
    }

    [Fact]
    public void More_than_256_glyphs_use_multiple_banks_without_reembedding()
    {
        var text = string.Concat(Enumerable.Range(0x3041, 86).Concat(Enumerable.Range(0x4e00, 250)).Select(char.ConvertFromUtf32));
        var ps = Encoding.ASCII.GetString(Render(Text(text)));
        Assert.Contains("/ERF0B1", ps);
        Assert.Single(Regex.Matches(ps, @"/sfnts \["));
        Assert.Equal(text.Length, Regex.Matches(ps, @"> show\n").Count);
        ValidateEmbeddedFonts(ps);
    }

    [Fact]
    public void Pages_empty_page_and_nonseekable_output_keep_ownership()
    {
        using var memory = new MemoryStream();
        using var output = new Nonseekable(memory);
        new PostScriptRenderer(Manager()).Render([Text("2", 2), Text("1", 1)], new(200, 100), output);
        var ps = Encoding.ASCII.GetString(memory.ToArray());
        Assert.Equal(2, Regex.Matches(ps, "grestore showpage").Count);
        Assert.Contains("%%Pages: 2\n", ps);
        Assert.Contains("%%PageBoundingBox: 0 0 200 100", ps);
        Assert.Contains("%%Pages: 1", Encoding.ASCII.GetString(Render()));
        Assert.True(memory.CanWrite);
    }

    [Fact]
    public async Task Unified_api_selects_mixed_paper_pages_and_creates_one_ps_artifact()
    {
        using var input = Workbook();
        using var memory = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.PostScript,
            FontOptions = new() { AllowSystemFonts = false },
        }, new SingleStreamOutputSink(new Nonseekable(memory)));
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("application/postscript", artifact.Descriptor.MediaType);
        Assert.Equal("ps", artifact.Descriptor.Kind);
        Assert.Equal(2, result.Pages.Count);
        var ps = Encoding.ASCII.GetString(memory.ToArray());
        Assert.Equal(2, Regex.Matches(ps, "grestore showpage").Count);
        Assert.Contains(PostScriptWriter.Number(result.Pages[0].WidthPoints), ps);
        Assert.Contains(PostScriptWriter.Number(result.Pages[1].WidthPoints), ps);
        input.Position = 0;
        using var selected = new MemoryStream();
        var selection = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.PostScript,
            Selection = new() { Pages = [2] },
            FontOptions = new() { AllowSystemFonts = false },
        }, new SingleStreamOutputSink(selected));
        Assert.Single(selection.Pages);
        Assert.Contains("%%Pages: 1", Encoding.ASCII.GetString(selected.ToArray()));
    }

    [Fact]
    public void Partial_alpha_and_image_masks_fail_with_distinct_diagnostics()
    {
        var failure = Assert.Throws<ConversionException>(() => Render(new FillRectangleCommand(1, new(0, 0, 20, 20), new(0, 0, 0, 128))));
        Assert.Equal("PostScriptTransparencyUnsupported", Assert.Single(failure.CollectedDiagnostics).Code);
        Assert.Throws<ConversionException>(() => Render(Text("透明") with { Style = CellStyle.Default with { Font = new("Noto Sans JP", 12, Color: new(0, 0, 0, 128)) } }));
        using var bitmap = new global::SkiaSharp.SKBitmap(2, 1);
        bitmap.SetPixel(0, 0, global::SkiaSharp.SKColors.Red);
        bitmap.SetPixel(1, 0, global::SkiaSharp.SKColors.Transparent);
        using var image = global::SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
        failure = Assert.Throws<ConversionException>(() => Render(new DrawImageCommand(1, new(0, 0, 20, 20), data.ToArray())));
        Assert.Equal("PostScriptImageTransparencyUnsupported", Assert.Single(failure.CollectedDiagnostics).Code);
        Assert.DoesNotContain("setrgbcolor", Encoding.ASCII.GetString(Render(new FillRectangleCommand(1, new(0, 0, 20, 20), new(0, 0, 0, 0)))));
    }

    [Theory]
    [InlineData("cff.otf")]
    [InlineData("variable.ttf")]
    [InlineData("colr.ttf")]
    public void Unsupported_fonts_fail_before_writing(string name)
    {
        var manager = new FontManager(new() { AllowSystemFonts = false, Registrations = [new("Test", Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats", name))] });
        using var output = new MemoryStream();
        var error = Assert.Throws<ConversionException>(() => new PostScriptRenderer(manager).Render([Text("A") with { Style = CellStyle.Default with { Font = new("Test", 12) } }], new(200, 100), output));
        Assert.Equal("PostScriptFontUnsupported", Assert.Single(error.CollectedDiagnostics).Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Cancellation_and_continuous_layout_do_not_commit()
    {
        using var input = Workbook();
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.RenderAsync(input,
            new() { OutputFormat = OutputFormat.PostScript }, new SingleStreamOutputSink(output), new(true)));
        Assert.Equal(0, output.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => ExcelConverter.RenderAsync(input,
            new() { OutputFormat = OutputFormat.PostScript, ImageLayout = ImageLayoutMode.Continuous }, new SingleStreamOutputSink(output)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Selected_collection_face_and_composite_glyphs_are_embedded(int index)
    {
        var manager = new FontManager(new() { AllowSystemFonts = false, Registrations = [new("Selected", Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats/collection-distinct.ttc")) { RegularCollectionIndex = index }] });
        using var output = new MemoryStream();
        new PostScriptRenderer(manager).Render([Text("A B") with { Style = CellStyle.Default with { Font = new("Selected", 12) } }], new(200, 100), output);
        ValidateEmbeddedFonts(Encoding.ASCII.GetString(output.ToArray()));
        var composite = Encoding.ASCII.GetString(Render(Text("é Å が")));
        ValidateEmbeddedFonts(composite);
    }

    [Theory]
    [InlineData(0x0002)]
    [InlineData(0x0100)]
    [InlineData(0x0200)]
    public void Embedding_and_subsetting_restrictions_are_diagnosed(int restrictions)
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats/static.ttf"));
        var face = new Core.Fonts.TrueType.SfntReader(data);
        var os2 = face.Get("OS/2").ToArray();
        Core.Fonts.TrueType.SfntWriter.Put16(os2, 8, (ushort)restrictions);
        var tables = face.Tables.ToDictionary(entry => entry.Key, entry => new ReadOnlyMemory<byte>(data, entry.Value.Offset, entry.Value.Length));
        tables["OS/2"] = os2;
        var path = Path.Combine(Path.GetTempPath(), "ps-restricted-" + Guid.NewGuid().ToString("N") + ".ttf");
        File.WriteAllBytes(path, Core.Fonts.TrueType.SfntWriter.Write(face.Version, tables, default));
        try
        {
            var manager = new FontManager(new() { AllowSystemFonts = false, Registrations = [new("Restricted", path)] });
            using var output = new MemoryStream();
            var failure = Assert.Throws<ConversionException>(() => new PostScriptRenderer(manager).Render([Text("A") with { Style = CellStyle.Default with { Font = new("Restricted", 12) } }], new(100, 100), output));
            Assert.Equal("PostScriptFontUnsupported", Assert.Single(failure.CollectedDiagnostics).Code);
            Assert.Equal(0, output.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Identical_family_and_face_labels_do_not_merge_different_physical_fonts()
    {
        var collection = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats/collection-distinct.ttc"));
        var regular = new ResolvedFont("Same", 400, false, "") { FaceId = "same", FontData = Core.Fonts.TrueType.FontFaceExtractor.Extract(collection, 0) };
        var bold = regular with { FontData = Core.Fonts.TrueType.FontFaceExtractor.Extract(collection, 1) };
        var text = Text("AB") with
        {
            TextLayout = new(new(30, 20), [new("AB", 30, 20, 15, [new(new("A", regular), 0, 15), new(new("B", bold), 15, 15)], false)]) { EffectiveFontSize = 12 },
        };
        var ps = Encoding.ASCII.GetString(Render(text));
        Assert.Equal(2, Regex.Matches(ps, @"/sfnts \[").Count);
        Assert.Contains("/ERF0B0", ps);
        Assert.Contains("/ERF1B0", ps);
        ValidateEmbeddedFonts(ps);
    }

    [Fact]
    public void Alternate_glyphs_for_the_same_unicode_name_use_independent_bank_charstrings()
    {
        var manager = new FontManager(new() { AllowSystemFonts = false, IvsFontStyle = IvsFontStyle.Mincho });
        using var output = new MemoryStream();
        new PostScriptRenderer(manager).Render([Text("辻辻\U000E0102") with { Style = CellStyle.Default with { Font = new("IPAmjMincho", 12) } }], new(200, 100), output);
        var ps = Encoding.ASCII.GetString(output.ToArray());
        Assert.Contains("/ERF0B1", ps);
        Assert.Contains("/uni8FBB", ps);
        ValidateEmbeddedFonts(ps);
    }

    [Fact]
    public void Original_text_survives_vertical_layout_and_many_metadata_chunks()
    {
        var original = "日本語";
        var ps = Encoding.ASCII.GetString(Render(Text(original) with { Style = CellStyle.Default with { TextRotation = 255 } }));
        Assert.Contains(Metadata(ps), record => record.Kind == 0 && record.Text == original);
        var longText = string.Concat(Enumerable.Repeat("日本語\U000E0102 ()\\\n", 300));
        ps = Encoding.ASCII.GetString(Render(Text(longText)));
        Assert.Contains(Metadata(ps), record => record.Kind == 0 && record.Text == longText);
        Assert.All(ps.Split('\n').Where(line => line.StartsWith("%ExcelRenderer-Text-v1:", StringComparison.Ordinal)), line => Assert.InRange(line.Length, 1, 255));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Output_failure_or_cancellation_aborts_once_and_releases_spool(bool cancel)
    {
        using var input = Workbook();
        using var cancellation = new CancellationTokenSource();
        using var output = new FailingOutput(cancel ? cancellation : null);
        var sink = new ObservedSink(output);
        var directory = Path.Combine(Path.GetTempPath(), "ps-spool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var request = new RenderRequest { OutputFormat = OutputFormat.PostScript, FontOptions = new() { AllowSystemFonts = false }, Buffering = new() { MemoryThresholdBytes = 1, TemporaryDirectory = directory } };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.RenderAsync(input, request, sink, cancellation.Token));
            else await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request, sink));
            Assert.Equal(1, sink.Aborts);
            Assert.Equal(0, sink.Completes);
            Assert.True(output.CanWrite);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Transparent_image_reports_sheet_page_and_does_not_commit()
    {
        using var input = Workbook();
        using var bitmap = new global::SkiaSharp.SKBitmap(2, 2);
        bitmap.Erase(new global::SkiaSharp.SKColor(200, 50, 40, 128));
        using var image = global::SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true))
            SdkWorkbook.Image(document.WorkbookPart!.WorksheetParts.First(), data.ToArray());
        input.Position = 0;
        using var output = new MemoryStream();
        var failure = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.PostScript }, new SingleStreamOutputSink(output)));
        var diagnostic = Assert.Single(failure.CollectedDiagnostics, value => value.Code == "PostScriptTransparencyUnsupported");
        Assert.Equal("a4", diagnostic.SheetName);
        Assert.Equal(1, diagnostic.SourcePageNumber);
        Assert.Empty(failure.CompletedArtifacts);
        Assert.Equal(0, output.Length);
    }

    private sealed class FailingOutput(CancellationTokenSource? cancellation) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (cancellation is null) throw new IOException("Injected output failure.");
            base.Write(buffer, offset, count);
            cancellation.Cancel();
        }
    }

    private sealed class ObservedSink(Stream stream) : IRenderOutputSink
    {
        public int Aborts { get; private set; }
        public int Completes { get; private set; }
        public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) => new(stream);
        public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) { Completes++; return default; }
        public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) { Aborts++; return default; }
    }

    internal static MemoryStream Workbook()
    {
        var stream = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "日本語 ABC")) { RowIndex = 1 }), new S.PageSetup { PaperSize = 9 }), SdkWorkbook.Styles(), name: "a4");
        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, true))
        {
            SdkWorkbook.AddSheet(document.WorkbookPart!, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "かな 漢字")) { RowIndex = 1 }), new S.PageSetup { PaperSize = 8, Orientation = S.OrientationValues.Landscape }), "a3 landscape");
        }
        stream.Position = 0;
        return stream;
    }

    internal static IEnumerable<(byte Kind, int Page, int Id, string Text)> Metadata(string ps)
    {
        var parts = Regex.Matches(ps, @"^%ExcelRenderer-Text-v1: (\d+) (\d+)/(\d+) ([A-Za-z0-9+/=]+)$", RegexOptions.Multiline);
        foreach (var group in parts.Cast<Match>().GroupBy(match => match.Groups[1].Value))
        {
            var ordered = group.OrderBy(match => int.Parse(match.Groups[2].Value)).ToArray();
            Assert.Equal(int.Parse(ordered[0].Groups[3].Value), ordered.Length);
            var encoded = string.Concat(ordered.Select(match => match.Groups[4].Value));
            using var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(encoded)), Encoding.UTF8);
            yield return (reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString());
        }
    }

    internal static void ValidateEmbeddedFonts(string ps)
    {
        foreach (Match match in Regex.Matches(ps, @"/sfnts \[(.*?)\] def", RegexOptions.Singleline))
        {
            using var font = new MemoryStream();
            foreach (Match chunk in Regex.Matches(match.Groups[1].Value, @"<([0-9A-F\s]+)>"))
            {
                var bytes = Convert.FromHexString(Regex.Replace(chunk.Groups[1].Value, @"\s", ""));
                Assert.InRange(bytes.Length, 1, 65535);
                Assert.Equal(0, bytes[^1]);
                font.Write(bytes, 0, bytes.Length - 1);
            }
            var data = font.ToArray();
            var sfnt = new Core.Fonts.TrueType.SfntReader(data);
            Core.Fonts.TrueType.TrueTypeFontValidator.Validate(sfnt, default);
            uint sum = 0;
            for (var offset = 0; offset < data.Length; offset += 4)
                sum = unchecked(sum + Core.Fonts.TrueType.SfntReader.U32(data, offset));
            Assert.Equal(0xB1B0AFBAu, sum);
        }
    }

    private sealed class Nonseekable(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
