using System.Globalization;
using System.Text;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Pdf.Structure;
using Xunit;
using ExcelRenderer.OpenXml.Reader.Input;
using UglyToad.PdfPig.Tokens;

namespace ExcelRenderer.Core.Tests;

public sealed class GeneratedPdfFinalizerTests
{
    [Theory]
    [InlineData("\n", false, false)]
    [InlineData("\n", false, true)]
    [InlineData("\r\n", false, false)]
    [InlineData("\r\n", false, true)]
    [InlineData("\r", false, false)]
    [InlineData("\r", false, true)]
    [InlineData("\n", true, false)]
    [InlineData("\n", true, true)]
    [InlineData("\r\n", true, false)]
    [InlineData("\r\n", true, true)]
    public async Task Structural_save_preserves_streams_generations_and_metadata(string newline, bool escapedNames, bool spill)
    {
        var sourceBytes = Fixture(newline, escapedNames);
        using var source = new SpillableBufferStream(new() { MemoryThresholdBytes = spill ? 1 : sourceBytes.Length, AllowTemporaryFiles = true });
        source.Write(sourceBytes);
        source.Position = 0;
        using var output = new MemoryStream();
        GeneratedPdfFinalizer.Write(source, output, [new(100.123, 99.456,
            [new(new(10, 20, 30, 10), "https://example.com?q=日本", null, null, null, "説明\n"),
             new(new(50, 20, 30, 10), null, 1, 12, 34, null)])], default);
        using var asyncSource = new SpillableBufferStream(new() { MemoryThresholdBytes = spill ? 1 : sourceBytes.Length, AllowTemporaryFiles = true });
        asyncSource.Write(sourceBytes);
        asyncSource.Position = 0;
        using var asyncOutput = new AsyncOnlyWriteStream();
        await GeneratedPdfFinalizer.WriteAsync(asyncSource, asyncOutput, [new(100.123, 99.456,
            [new(new(10, 20, 30, 10), "https://example.com?q=日本", null, null, null, "説明\n"),
             new(new(50, 20, 30, 10), null, 1, 12, 34, null)])], default);
        Assert.Equal(output.ToArray(), asyncOutput.Bytes);
        Assert.Equal(0, asyncOutput.Flushes);
        Assert.False(asyncOutput.Disposed);
        Assert.NotEqual(sourceBytes, output.ToArray());
        Assert.DoesNotContain("/Prev", Encoding.Latin1.GetString(output.ToArray()));
        var updated = new GeneratedPdfFile(new MemoryStream(output.ToArray()));
        Assert.Equal(new PdfReference(3, 2), Assert.Single(updated.Pages));
        Assert.Equal(9, updated.Size);
        Assert.Equal(new PdfReference(1), updated.Trailer.Get("/Root"));
        Assert.Equal(new PdfReference(5), updated.Trailer.Get("/Info"));
        Assert.NotNull(updated.Trailer.Get("/ID"));
        Assert.Equal(new PdfReference(4), updated.Map(updated.Pages[0]).Get("/Contents"));
        Assert.NotNull(updated.Map(updated.Trailer.Get("/Root")).Get("/Names"));
        var image = updated.Read(new(6));
        using var copied = new MemoryStream();
        updated.CopyStream(image, copied, default);
        Assert.Equal(new byte[] { 9, 128, 255 }, copied.ToArray());
        Assert.Equal(new PdfLiteral("true"), ((PdfMap)image.Value).Get("/Interpolate"));
        using var independent = TestSupport.Open(output.ToArray());
        var page = independent.GetPage(1);
        Assert.Equal(100.123, page.Width, 5);
        Assert.Equal(99.456, page.Height, 5);
        var annotations = TestSupport.Entry<ArrayToken>(independent, page.Dictionary, "Annots").Data
            .Select(token => TestSupport.Token<DictionaryToken>(independent, token)).ToArray();
        Assert.Equal(2, annotations.Length);
        Assert.Equal("説明", TestSupport.PdfString(annotations[0].Data["Contents"]));
        var action = TestSupport.Entry<DictionaryToken>(independent, annotations[0], "A");
        Assert.Equal("https://example.com?q=日本", TestSupport.PdfString(action.Data["URI"]));
        var destination = TestSupport.Entry<ArrayToken>(independent, annotations[1], "Dest");
        Assert.Equal(page.Dictionary, TestSupport.Token<DictionaryToken>(independent, destination.Data[0]));
        Assert.Equal(65.456, ((NumericToken)destination.Data[3]).Double, 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(15)]
    [InlineData(100)]
    public void Truncated_input_is_rejected_without_writing(int missingBytes)
    {
        var bytes = Fixture();
        var length = missingBytes == 0 ? 0 : bytes.Length - missingBytes;
        using var source = new MemoryStream(bytes.Take(length).ToArray());
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => GeneratedPdfFinalizer.Write(source, output, [new(100, 100, [])], default));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("length")]
    [InlineData("signature")]
    [InlineData("encrypted")]
    [InlineData("object-stream")]
    [InlineData("xref-stream")]
    public void Unsupported_or_corrupt_structure_fails_closed(string defect)
    {
        using var source = new MemoryStream(Fixture(defect: defect));
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => GeneratedPdfFinalizer.Write(source, output, [new(100, 100, [])], default));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Literal_hex_name_and_nested_syntax_round_trips_without_changing_strings()
    {
        var bytes = Encoding.Latin1.GetBytes("<< /Na#6De (a\\(b\\) \\376\\377) /Hex <FEFF65E5> /Nested [true null -1.25 [3 2 R]] /Raw (\u00fe\u00ff) >>");
        var value = new PdfSyntaxReader(new MemoryStream(bytes)).Value();
        Assert.IsType<PdfMap>(value);
        var roundTrip = Encoding.Latin1.GetBytes(value.Serialize());
        var reparsed = (PdfMap)new PdfSyntaxReader(new MemoryStream(roundTrip)).Value();
        Assert.NotNull(reparsed.Get("/Name"));
        Assert.Equal("(a\\(b\\) \\376\\377)", ((PdfString)reparsed.Get("/Name")!).Token);
        Assert.Equal("(\u00fe\u00ff)", ((PdfString)reparsed.Get("/Raw")!).Token);
    }

    [Fact]
    public void Bounded_dictionary_and_page_depth_reject_excessive_nesting()
    {
        var text = new string('[', 66) + "1" + new string(']', 66);
        Assert.Throws<InvalidDataException>(() => new PdfSyntaxReader(new MemoryStream(Encoding.ASCII.GetBytes(text))).Value());
        Assert.Throws<InvalidDataException>(() => new PdfSyntaxReader(new MemoryStream(Encoding.ASCII.GetBytes("<< /Bad#X1 1 >>"))).Value());
    }

    [Theory]
    [InlineData(200000, 0)]
    [InlineData(0, 70000)]
    [InlineData(0, 64000)]
    public async Task Large_payload_and_syntax_preserve_bytes_with_short_async_reads(int payload, int metadata)
    {
        var bytes = Fixture(payloadSize: payload, metadataSize: metadata);
        using var source = new ShortReadStream(bytes);
        using var expected = new MemoryStream();
        GeneratedPdfFinalizer.Write(source, expected, [new(100, 100, [])], default);
        using var output = new AsyncOnlyWriteStream();
        await GeneratedPdfFinalizer.WriteAsync(source, output, [new(100, 100, [])], default);
        Assert.Equal(expected.ToArray(), output.Bytes);
        Assert.Equal(1, output.MaxActive);
        Assert.InRange(output.MaxWriteBytes, 1, 81920);
        using var parsed = TestSupport.Open(output.Bytes);
        Assert.Single(parsed.GetPages());
    }

    [Fact]
    public async Task Range_copy_detects_truncation_and_empty_transfer_buffer()
    {
        using var source = new ShortReadStream(Fixture());
        var pdf = new GeneratedPdfFile(source);
        var record = pdf.Read(new(4));
        using var output = new AsyncOnlyWriteStream();
        await Assert.ThrowsAsync<ArgumentException>(() => pdf.CopyStreamAsync(record, output, default, []));
        source.Truncated = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => pdf.CopyStreamAsync(record, output, default, new byte[32]));
        Assert.Empty(output.Bytes);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("length")]
    [InlineData("encrypted")]
    public async Task Async_save_rejects_invalid_input_before_output(string defect)
    {
        using var source = new MemoryStream(Fixture(defect: defect));
        using var output = new AsyncOnlyWriteStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => GeneratedPdfFinalizer.WriteAsync(source, output, [new(100, 100, [])], default));
        Assert.Empty(output.Bytes);
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal bool Truncated { get; set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Truncated ? ValueTask.FromResult(0) : base.ReadAsync(buffer[..Math.Min(13, buffer.Length)], cancellationToken);
    }

    [Theory]
    [InlineData(8191, false, 1)]
    [InlineData(8192, true, 2)]
    [InlineData(100000, false, 3)]
    [InlineData(200000, true, int.MaxValue)]
    public void Indirect_lengths_and_out_of_order_reads_survive_copy_and_nonseekable_output(int length, bool spill, int maximumRead)
    {
        var payload = string.Concat(Enumerable.Repeat("\0\u0080\u00ff endstream\r\nxref /Length 0 obj % fake\n", (length / 30) + 1))[..length];
        var bytes = Fixture(payload: payload, indirectLength: true);
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using (var spool = new SpillableBufferStream(new() { MemoryThresholdBytes = spill ? 1 : bytes.Length, AllowTemporaryFiles = true, TemporaryDirectory = directory.FullName }))
            {
                spool.Write(bytes);
                using var input = new PdfReadTestStream(bytes, maximumRead);
                var pdf = new GeneratedPdfFile(input);
                Assert.IsType<PdfMap>(pdf.Read(new(6)).Value);
                var content = pdf.Read(new(4));
                Assert.Equal(length, content.StreamLength);
                using var copied = new MemoryStream();
                pdf.CopyStream(content, copied, default);
                Assert.Equal(Encoding.Latin1.GetBytes(payload), copied.ToArray());
                // This object was not cached before copying. Its parse must restore the copy-end cursor.
                Assert.IsType<PdfMap>(pdf.Read(new(5)).Value);
                Assert.Equal(new PdfNumber(length), pdf.Read(new(7)).Value);
                using var image = new MemoryStream();
                pdf.CopyStream(pdf.Read(new(6)), image, default);
                Assert.Equal(new byte[] { 9, 128, 255 }, image.ToArray());
                Assert.Equal(0, input.ReadByteCalls);

                using var output = new WriteOnlyOutput();
                GeneratedPdfFinalizer.Write(spool, output, [new(100.123, 99.456, [])], default);
                Assert.True(spool.CanRead);
                using var rewritten = new MemoryStream(output.Bytes);
                var updated = new GeneratedPdfFile(rewritten);
                using var finalPayload = new MemoryStream();
                updated.CopyStream(updated.Read(new(4)), finalPayload, default);
                Assert.Equal(Encoding.Latin1.GetBytes(payload), finalPayload.ToArray());
                Assert.True(rewritten.CanRead);
            }

            Assert.Empty(directory.GetFiles());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Range_copy_rejects_truncation_and_checks_cancellation_between_blocks()
    {
        var payload = new string('x', 200000);
        var bytes = Fixture(payload: payload, indirectLength: true);
        using var source = new MemoryStream(bytes);
        var pdf = new GeneratedPdfFile(source);
        var record = pdf.Read(new(4));
        Assert.Throws<ArgumentException>(() => pdf.CopyStream(record, Stream.Null, default, []));
        using var cancel = new CancellationTokenSource();
        using var output = new WriteOnlyOutput(cancel);
        Assert.Throws<OperationCanceledException>(() => pdf.CopyStream(record, output, cancel.Token, new byte[1024]));
        Assert.Equal(1024, output.Bytes.Length);
        source.SetLength(record.StreamOffset + 10);
        Assert.Throws<InvalidDataException>(() => pdf.CopyStream(record, Stream.Null, default));
    }

    private static byte[] Fixture(string newline = "\n", bool escapedNames = false, string? defect = null, int payloadSize = 0, int metadataSize = 0, string? payload = null, bool indirectLength = false)
    {
        var content = payload ?? new string(' ', payloadSize) + "% /Page xref trailer startxref endstream obj dictionary injection must remain a comment\nq\nQ\n";
        var objects = new Dictionary<int, (int Generation, string Value)>
        {
            [1] = (0, "<< /Type /Catalog /Pages 2 0 R /Names << /Dests << /Names [(back) [3 2 R /XYZ 0 100 0]] >> >> >>"),
            [2] = (0, "<< /Type /Pages /Kids [" + (defect == "cycle" ? "2 0 R" : "3 2 R") + "] /Count 1 /MediaBox [0 0 100 100] >>"),
            [3] = (2, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << >> /Custom [(preserve me)] >>"),
            [4] = (0, "<< /Length " + (defect == "length" ? "999999999" : indirectLength ? "7 0 R" : Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture)) + " >>\nstream\n" + content + "endstream"),
            [5] = (0, "<< /Extra (" + new string('x', metadataSize) + ") /Title (metadata\\(x\\) \\376\\377) /Producer (Skia/PDF) >>"),
            [6] = (0, defect == "signature" ? "<< /Type /Sig /ByteRange [0 1 2 3] >>" : defect == "object-stream" ? "<< /Type /ObjStm >>" : "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB /Length 3 >>\nstream\n\u0009\u0080\u00ff\nendstream"),
        };
        if (indirectLength) objects.Add(7, (0, Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture)));
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.7\n");
        var offsets = new List<long> { 0 };
        foreach (var item in objects)
        {
            offsets.Add(output.Position);
            var value = escapedNames ? item.Value.Value.Replace("/Type", "/Ty#70e").Replace("/Pages", "/Pa#67es") : item.Value.Value;
            // Stream length is calculated from LF bytes, so only structural line endings are varied.
            if (item.Key is not (4 or 6)) value = value.Replace("\n", newline);
            Write($"{item.Key} {item.Value.Generation} obj" + newline + value + newline + "endobj" + newline);
        }
        var xref = output.Position;
        Write((defect == "xref-stream" ? "unsupported-xref-stream" : "xref") + newline + "0 " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + newline + "0000000000 65535 f " + newline);
        foreach (var item in objects)
            Write(offsets[item.Key].ToString("D10", CultureInfo.InvariantCulture) + " " + item.Value.Generation.ToString("D5", CultureInfo.InvariantCulture) + " n " + newline);
        Write("trailer" + newline + "<< /Size " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R /Info 5 0 R /ID [<01234567><89ABCDEF>]" + (defect == "encrypted" ? " /Encrypt 5 0 R" : string.Empty) + ">>" + newline + "startxref" + newline + xref.ToString(CultureInfo.InvariantCulture) + newline + "%%EOF" + newline);
        return output.ToArray();
    }
    private sealed class WriteOnlyOutput(CancellationTokenSource? cancel = null) : Stream
    {
        private readonly MemoryStream bytes = new();
        internal byte[] Bytes => bytes.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { bytes.Write(buffer, offset, count); cancel?.Cancel(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) bytes.Dispose();
            base.Dispose(disposing);
        }
    }
}
