using System.Globalization;
using System.Text;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Pdf.Structure;
using ExcelRenderer.TestFixtures;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class GeneratedPdfFinalizerTests
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\r", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    public void Structural_save_preserves_streams_generations_and_metadata(string newline, bool escapedNames)
    {
        var sourceBytes = Fixture(newline, escapedNames);
        using var source = new MemoryStream(sourceBytes);
        using var output = new MemoryStream();
        GeneratedPdfFinalizer.Write(source, output, [new(100.123, 99.456,
            [new(new(10, 20, 30, 10), "https://example.com?q=日本", null, null, null, "説明\n"),
             new(new(50, 20, 30, 10), null, 1, 12, 34, null)])], default);
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
        using var independent = PdfTestFile.Open(new MemoryStream(output.ToArray()));
        Assert.Equal(100.123, independent.Pages[0].Width.Point, 5);
        Assert.Equal(99.456, independent.Pages[0].Height.Point, 5);
        Assert.Equal(2, independent.Pages[0].Annotations.Count);
        Assert.Equal("説明", independent.Pages[0].Annotations[0].Elements.GetString("/Contents"));
        Assert.Equal("https://example.com?q=日本", independent.Pages[0].Annotations[0].Elements.GetDictionary("/A")!.Elements.GetString("/URI"));
        var destination = independent.Pages[0].Annotations[1].Elements.GetArray("/Dest")!;
        Assert.Same(independent.Pages[0], ((PdfTestReference)destination.Elements[0]).Value);
        Assert.Equal(65.456, ((PdfTestNumber)destination.Elements[3]).Value, 5);
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

    private static byte[] Fixture(string newline = "\n", bool escapedNames = false, string? defect = null)
    {
        var content = "% /Page xref trailer startxref endstream obj dictionary injection must remain a comment\nq\nQ\n";
        var objects = new Dictionary<int, (int Generation, string Value)>
        {
            [1] = (0, "<< /Type /Catalog /Pages 2 0 R /Names << /Dests << /Names [(back) [3 2 R /XYZ 0 100 0]] >> >> >>"),
            [2] = (0, "<< /Type /Pages /Kids [" + (defect == "cycle" ? "2 0 R" : "3 2 R") + "] /Count 1 /MediaBox [0 0 100 100] >>"),
            [3] = (2, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << >> /Custom [(preserve me)] >>"),
            [4] = (0, "<< /Length " + (defect == "length" ? "999999999" : Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture)) + " >>\nstream\n" + content + "endstream"),
            [5] = (0, "<< /Title (metadata\\(x\\) \\376\\377) /Producer (Skia/PDF) >>"),
            [6] = (0, defect == "signature" ? "<< /Type /Sig /ByteRange [0 1 2 3] >>" : defect == "object-stream" ? "<< /Type /ObjStm >>" : "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB /Length 3 >>\nstream\n\u0009\u0080\u00ff\nendstream"),
        };
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
        Write((defect == "xref-stream" ? "unsupported-xref-stream" : "xref") + newline + "0 7" + newline + "0000000000 65535 f " + newline);
        foreach (var item in objects)
            Write(offsets[item.Key].ToString("D10", CultureInfo.InvariantCulture) + " " + item.Value.Generation.ToString("D5", CultureInfo.InvariantCulture) + " n " + newline);
        Write("trailer" + newline + "<< /Size 7 /Root 1 0 R /Info 5 0 R /ID [<01234567><89ABCDEF>]" + (defect == "encrypted" ? " /Encrypt 5 0 R" : string.Empty) + ">>" + newline + "startxref" + newline + xref.ToString(CultureInfo.InvariantCulture) + newline + "%%EOF" + newline);
        return output.ToArray();
    }
}
