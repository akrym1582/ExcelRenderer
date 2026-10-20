using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Reads the classic xref format emitted by pinned Skia; never accepts arbitrary user PDFs.</summary>
internal sealed class GeneratedPdfFile
{
    private readonly PdfSyntaxReader reader;
    private readonly Dictionary<int, (long Offset, int Generation, bool Active)> offsets = new();
    private readonly Dictionary<int, ObjectRecord> objects = new();
    private readonly HashSet<int> loading = new();

    /// <summary>Initializes a new instance of the <see cref="GeneratedPdfFile"/> class.</summary>
    /// <param name="input">Our completed, seekable Skia spool.</param>
    internal GeneratedPdfFile(Stream input)
    {
        if (!input.CanSeek)
        {
            throw new InvalidDataException("A complete seekable generated PDF is required.");
        }

        reader = new(input);
        if (reader.Length < 20)
        {
            throw new InvalidDataException("A complete seekable generated PDF is required.");
        }

        reader.Position = 0;
        var header = new byte[8];
        reader.ReadExactly(header);
        Header = Encoding.ASCII.GetString(header);
        if (!Header.StartsWith("%PDF-1.", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected generated PDF header.");
        }

        reader.Position = Math.Max(0, reader.Length - 1024);
        var tail = new byte[checked((int)(reader.Length - reader.Position))];
        reader.ReadExactly(tail);
        var end = Encoding.ASCII.GetString(tail);
        var marker = end.LastIndexOf("startxref", StringComparison.Ordinal);
        if (marker < 0 || !end.Substring(marker).Contains("%%EOF", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Generated PDF has no final xref marker.");
        }

        reader.Position = reader.Length - tail.Length + marker;
        reader.Expect("startxref");
        StartXref = Integer(reader.Token(), reader.Length - 1);
        var next = StartXref;
        var visited = new HashSet<long>();
        PdfMap? latest = null;
        while (true)
        {
            if (next < 0 || next >= reader.Length || !visited.Add(next) || visited.Count > 32)
            {
                throw new InvalidDataException("Invalid or cyclic generated PDF xref chain.");
            }

            reader.Position = next;

            // Skia 4.151.0 emits classic tables, no xref/object streams. Reject other forms.
            reader.Expect("xref");
            string token;
            while ((token = reader.Token()) != "trailer")
            {
                var first = checked((int)Integer(token, 1000000));
                var count = checked((int)Integer(reader.Token(), 1000000));
                if ((long)first + count > 1000001)
                {
                    throw new InvalidDataException("Generated PDF xref exceeds the object limit.");
                }

                for (var index = 0; index < count; index++)
                {
                    var offset = Integer(reader.Token(), reader.Length - 1);
                    var generation = checked((int)Integer(reader.Token(), 65535));
                    var flag = reader.Token();
                    if (flag is not ("n" or "f"))
                    {
                        throw new InvalidDataException("Malformed PDF xref entry.");
                    }

                    if (!offsets.ContainsKey(first + index))
                    {
                        offsets.Add(first + index, (offset, generation, flag == "n"));
                    }
                }
            }

            var trailer = reader.Value() as PdfMap ?? throw new InvalidDataException("Generated PDF trailer is missing.");
            latest ??= trailer;
            if (trailer.Get("/Encrypt") is not null || trailer.Get("/XRefStm") is not null)
            {
                throw new InvalidDataException("Encrypted or hybrid PDFs are not generated-format inputs.");
            }

            if (trailer.Get("/Prev") is not PdfNumber previous)
            {
                break;
            }

            next = ExactInteger(previous.Value, reader.Length - 1);
        }

        Trailer = latest!;
        Size = checked((int)ExactInteger((Trailer.Get("/Size") as PdfNumber)?.Value ?? 0, 1000001));
        if (Size <= 0 || offsets.Keys.Any(number => number >= Size))
        {
            throw new InvalidDataException("Generated PDF trailer Size is inconsistent.");
        }

        var root = Map(Trailer.Get("/Root"));
        if (root.Get("/AcroForm") is not null || root.Get("/Perms") is not null)
        {
            throw new InvalidDataException("Forms or signed PDFs are not generated-format inputs.");
        }

        var pages = new List<PdfReference>();
        WalkPages(root.Get("/Pages"), new HashSet<int>(), pages);
        Pages = pages;
    }

    /// <summary>Gets the original generated PDF version header.</summary>
    internal string Header { get; }

    /// <summary>Gets the newest xref byte offset.</summary>
    internal long StartXref { get; }

    /// <summary>Gets the preserved trailer.</summary>
    internal PdfMap Trailer { get; }

    /// <summary>Gets the next unused object number.</summary>
    internal int Size { get; }

    /// <summary>Gets leaf page references in tree order.</summary>
    internal IReadOnlyList<PdfReference> Pages { get; }

    /// <summary>Gets all active indirect object references.</summary>
    internal IEnumerable<PdfReference> References => offsets.Where(entry => entry.Value.Active).Select(entry => new PdfReference(entry.Key, entry.Value.Generation));

    /// <summary>Reads a dictionary or resolves its indirect reference.</summary>
    /// <param name="value">The value or reference.</param>
    /// <returns>The original dictionary.</returns>
    internal PdfMap Map(PdfValue? value) => Resolve(value) as PdfMap ?? throw new InvalidDataException("Expected generated PDF dictionary.");

    /// <summary>Resolves one indirect value.</summary>
    /// <param name="value">The value or reference.</param>
    /// <returns>The direct value.</returns>
    internal PdfValue? Resolve(PdfValue? value) => value is PdfReference reference ? Read(reference).Value : value;

    /// <summary>Reads one bounded indirect object and records stream boundaries without decoding it.</summary>
    /// <param name="reference">The exact object and generation.</param>
    /// <returns>The parsed object.</returns>
    internal ObjectRecord Read(PdfReference reference)
    {
        if (objects.TryGetValue(reference.Number, out var cached))
        {
            if (cached.Reference != reference)
            {
                throw new InvalidDataException("PDF object generation differs.");
            }

            return cached;
        }

        if (!offsets.TryGetValue(reference.Number, out var entry) || !entry.Active || entry.Generation != reference.Generation || (!loading.Add(reference.Number) || loading.Count > 64))
        {
            throw new InvalidDataException("Invalid or cyclic PDF object reference.");
        }

        var saved = reader.Position;
        try
        {
            reader.Position = entry.Offset;
            if (Integer(reader.Token(), 1000000) != reference.Number || Integer(reader.Token(), 65535) != reference.Generation)
            {
                throw new InvalidDataException("PDF object header differs from its xref entry.");
            }

            reader.Expect("obj");
            var value = reader.Value();
            if (value is PdfMap map && map.Get("/Type") is PdfName { Value: "/ObjStm" or "/XRef" })
            {
                throw new InvalidDataException("Object and xref streams are unsupported generated-format inputs.");
            }

            var token = reader.Token();
            long streamOffset = 0;
            long streamLength = 0;
            if (token == "stream" && value is PdfMap dictionary)
            {
                reader.StreamNewline();
                streamOffset = reader.Position;
                streamLength = ExactInteger((Resolve(dictionary.Get("/Length")) as PdfNumber)?.Value ?? -1, reader.Length);
                if (streamLength > reader.Length - streamOffset)
                {
                    throw new InvalidDataException("Truncated generated PDF stream.");
                }

                reader.Position = streamOffset + streamLength;
                reader.Expect("endstream");
                token = reader.Token();
            }

            if (token != "endobj")
            {
                throw new InvalidDataException("Generated PDF object is not terminated.");
            }

            var result = new ObjectRecord(reference, value, streamOffset, streamLength);
            objects.Add(reference.Number, result);
            return result;
        }
        finally
        {
            loading.Remove(reference.Number);
            reader.Position = saved;
        }
    }

    /// <summary>Copies an existing stream byte-for-byte into its single output object.</summary>
    /// <param name="record">The original object.</param>
    /// <param name="output">The destination.</param>
    /// <param name="token">The cancellation token.</param>
    /// <param name="buffer">An optional shared transfer buffer.</param>
    internal void CopyStream(ObjectRecord record, Stream output, CancellationToken token, byte[]? buffer = null) =>
        reader.CopyRange(record.StreamOffset, record.StreamLength, output, token, buffer);

    private static long Integer(string token, long maximum) => long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result <= maximum ? result : throw new InvalidDataException("Invalid generated PDF integer.");

    private static long ExactInteger(double value, long maximum) => value >= 0 && value <= maximum && value == Math.Truncate(value) ? (long)value : throw new InvalidDataException("Invalid generated PDF offset or length.");

    private void WalkPages(PdfValue? value, HashSet<int> visited, List<PdfReference> pages, int depth = 0)
    {
        if (value is not PdfReference reference || !visited.Add(reference.Number) || visited.Count > 100000 || depth > 64)
        {
            throw new InvalidDataException("Generated PDF page tree is cyclic or exceeds its limit.");
        }

        var dictionary = Map(reference);
        if (dictionary.Get("/Type") is PdfName { Value: "/Page" })
        {
            pages.Add(reference);
            return;
        }

        if (dictionary.Get("/Type") is not PdfName { Value: "/Pages" } || Resolve(dictionary.Get("/Kids")) is not PdfArray kids)
        {
            throw new InvalidDataException("Invalid generated PDF page tree node.");
        }

        foreach (var child in kids.Items)
        {
            WalkPages(child, visited, pages, depth + 1);
        }
    }

    /// <summary>Records object identity, syntax and the exact encoded stream range.</summary>
    /// <param name="Reference">The object identity.</param>
    /// <param name="Value">The preserved value.</param>
    /// <param name="StreamOffset">The byte offset, or zero for nonstream objects.</param>
    /// <param name="StreamLength">The encoded stream length.</param>
    internal sealed record ObjectRecord(PdfReference Reference, PdfValue Value, long StreamOffset, long StreamLength);
}
