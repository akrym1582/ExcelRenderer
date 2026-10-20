using System.Text;
using ExcelRenderer.Core.Pdf.Structure;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class PdfSyntaxReaderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8192)]
    public void Values_and_stream_newlines_cross_every_buffer_boundary(int bufferSize)
    {
        const string syntax = "<< /Na#6De (a\\(b\\) (nested)\\\r\ncontinued) /Hex <FE FF 01> /Values [3 2 R -1.25 .5 1 2 true null] % comment\r\n /End /OK >>";
        for (var split = 0; split < syntax.Length; split++)
        {
            var padding = (bufferSize - (split % bufferSize)) % bufferSize;
            var bytes = Encoding.Latin1.GetBytes(new string(' ', padding) + syntax + " stream\r\nendstream");
            using var input = new PdfReadTestStream(bytes);
            var reader = new PdfSyntaxReader(input, bufferSize);
            var map = Assert.IsType<PdfMap>(reader.Value());
            Assert.Equal("(a\\(b\\) (nested)\\\r\ncontinued)", Assert.IsType<PdfString>(map.Get("/Name")).Token);
            Assert.Equal("<FE FF 01>", Assert.IsType<PdfString>(map.Get("/Hex")).Token);
            var values = Assert.IsType<PdfArray>(map.Get("/Values")).Items;
            Assert.Equal(new PdfReference(3, 2), values[0]);
            Assert.Equal(new PdfNumber(-1.25), values[1]);
            Assert.Equal(new PdfNumber(.5), values[2]);
            Assert.Equal(new PdfNumber(1), values[3]);
            Assert.Equal(new PdfNumber(2), values[4]);
            reader.Expect("stream");
            reader.StreamNewline();
            reader.Expect("endstream");
            Assert.Equal(bytes.Length, reader.Position);
            Assert.Throws<InvalidDataException>(() => reader.Token());
            Assert.Equal(0, input.ReadByteCalls);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Short_reads_preserve_numbers_references_and_saved_positions(int maximum)
    {
        using var input = new PdfReadTestStream(Encoding.ASCII.GetBytes("1 2 true 3 2 R /Name [false null]"), maximum);
        var reader = new PdfSyntaxReader(input);
        Assert.Equal(new PdfNumber(1), reader.Value());
        var saved = reader.Position;
        Assert.Equal(new PdfNumber(2), reader.Value());
        Assert.Equal(new PdfLiteral("true"), reader.Value());
        Assert.Equal(new PdfReference(3, 2), reader.Value());
        Assert.Equal(new PdfName("/Name"), reader.Value());
        Assert.IsType<PdfArray>(reader.Value());
        reader.Position = saved;
        Assert.Equal(new PdfNumber(2), reader.Value());
        Assert.Equal(0, input.ReadByteCalls);
    }

    [Fact]
    public void Lookahead_and_rewinds_within_buffer_do_not_touch_base_cursor()
    {
        using var input = new PdfReadTestStream(Encoding.ASCII.GetBytes("1 2 true /Name false"));
        var reader = new PdfSyntaxReader(input);
        Assert.Equal(new PdfNumber(1), reader.Value());
        var reads = input.Reads;
        var positions = input.PositionSets;
        for (var index = 0; index < 20; index++)
        {
            reader.Position = 0;
            Assert.Equal(new PdfNumber(1), reader.Value());
            Assert.Equal(new PdfNumber(2), reader.Value());
            Assert.Equal(new PdfLiteral("true"), reader.Value());
            reader.Position = reader.Length;
            Assert.Throws<InvalidDataException>(() => reader.Token());
        }
        Assert.Equal(reads, input.Reads);
        Assert.Equal(positions, input.PositionSets);
        Assert.Equal(0, input.ReadByteCalls);
        Assert.Equal(1, input.LengthGets);
        Assert.InRange(input.PositionGets, 0, 1);
    }

    [Fact]
    public void Long_contiguous_tokens_use_block_reads_and_support_distant_rewinds()
    {
        var token = new string('a', 50000);
        var bytes = Encoding.ASCII.GetBytes(token + " " + token + " ");
        using var input = new PdfReadTestStream(bytes);
        var reader = new PdfSyntaxReader(input);
        Assert.Equal(token, reader.Token());
        var end = reader.Position;
        Assert.Equal(token, reader.Token());
        Assert.InRange(input.Reads, 1, 20);
        Assert.InRange(input.PositionSets + input.Seeks, 0, 2);
        Assert.Equal(0, input.ReadByteCalls);
        Assert.Equal(1, input.LengthGets);
        reader.Position = 0;
        Assert.Equal(token, reader.Token());
        reader.Position = end;
        Assert.Equal(token, reader.Token());
        reader.Position = long.MaxValue;
        Assert.Throws<InvalidDataException>(() => reader.Token());
        reader.Position = 0;
        Assert.Equal(token, reader.Token());
    }

    [Theory]
    [InlineData("<< /A 1 >")]
    [InlineData("(unterminated\\")]
    [InlineData("[1 2")]
    [InlineData("<0x>")]
    [InlineData("/Bad#Q0")]
    [InlineData("<< /A 1 /A 2 >>")]
    public void Truncated_or_malformed_values_fail_with_small_and_default_buffers(string syntax)
    {
        foreach (var size in new[] { 1, 3, 8192 })
        {
            using var input = new PdfReadTestStream(Encoding.ASCII.GetBytes(syntax), 2);
            Assert.Throws<InvalidDataException>(() => new PdfSyntaxReader(input, size).Value());
        }
    }

    [Fact]
    public void Exact_reads_copy_ranges_and_eof_restore_share_one_logical_cursor()
    {
        var bytes = Encoding.ASCII.GetBytes("header 123 /Name tail");
        using var input = new PdfReadTestStream(bytes, 3);
        var reader = new PdfSyntaxReader(input, 7);
        var header = new byte[6];
        reader.ReadExactly(header);
        Assert.Equal("header", Encoding.ASCII.GetString(header));
        Assert.Equal(new PdfNumber(123), reader.Value());
        var saved = reader.Position;
        using var output = new MemoryStream();
        reader.CopyRange(0, 6, output, default, new byte[2]);
        Assert.Equal(header, output.ToArray());
        Assert.Equal(6, reader.Position);
        reader.Position = saved;
        Assert.Equal(new PdfName("/Name"), reader.Value());
        reader.Position = bytes.Length;
        reader.ReadExactly([]);
        Assert.Throws<InvalidDataException>(() => reader.ReadExactly(new byte[1]));
        reader.Position = 0;
        reader.ReadExactly(header);
        Assert.Equal("header", Encoding.ASCII.GetString(header));
        Assert.True(input.CanRead);
    }

    [Fact]
    public void Invalid_buffer_positions_ranges_and_physically_truncated_reads_fail()
    {
        using var input = new PdfReadTestStream([1], reportedLength: 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSyntaxReader(input, 0));
        var reader = new PdfSyntaxReader(input);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Position = -1);
        Assert.Throws<InvalidDataException>(() => reader.ReadExactly(new byte[2]));
        Assert.Throws<InvalidDataException>(() => reader.CopyRange(0, 2, Stream.Null, default));
        Assert.Throws<InvalidDataException>(() => reader.CopyRange(-1, 1, Stream.Null, default));
        Assert.Throws<InvalidDataException>(() => reader.CopyRange(0, -1, Stream.Null, default));
        Assert.Throws<InvalidDataException>(() => reader.CopyRange(3, 0, Stream.Null, default));
        Assert.Throws<InvalidDataException>(() => reader.CopyRange(1, long.MaxValue, Stream.Null, default));
    }
}
