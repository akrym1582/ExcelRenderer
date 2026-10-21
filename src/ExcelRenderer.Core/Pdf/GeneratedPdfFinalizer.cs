using System.Globalization;
using System.Text;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf.Structure;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Writes bounded structural corrections to a PDF produced by the pinned Skia backend.</summary>
internal static class GeneratedPdfFinalizer
{
    /// <summary>Writes each active object and encoded stream once with corrected dictionaries and a classic xref.</summary>
    /// <param name="source">The complete generated spool.</param>
    /// <param name="output">The caller-owned destination.</param>
    /// <param name="pages">Exact page dimensions and resolved links.</param>
    /// <param name="token">The cancellation token.</param>
    internal static void Write(Stream source, Stream output, IReadOnlyList<PageCorrection> pages, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pdf = new GeneratedPdfFile(source);
        var buffer = new byte[81920];
        foreach (var element in Elements(pdf, pages, token))
        {
            token.ThrowIfCancellationRequested();
            if (element.Bytes is { } bytes)
            {
                output.Write(bytes, 0, bytes.Length);
            }
            else
            {
                pdf.CopyStream(element.Stream!, output, token, buffer);
            }
        }

        Rendering.ConversionMetrics.Report("pdfSave", 1);
    }

    /// <summary>Writes corrected syntax and original payloads sequentially using only asynchronous destination I/O.</summary>
    /// <param name="source">The complete generated spool.</param>
    /// <param name="output">The caller-owned, possibly nonseekable destination.</param>
    /// <param name="pages">Exact page dimensions and resolved links.</param>
    /// <param name="token">The conversion token.</param>
    /// <returns>The completion of all destination writes; does not flush or close the destination.</returns>
    internal static async Task WriteAsync(Stream source, Stream output, IReadOnlyList<PageCorrection> pages, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pdf = new GeneratedPdfFile(source);
        var syntax = new byte[65536];
        var transfer = new byte[81920];
        var pending = 0;
        foreach (var element in Elements(pdf, pages, token))
        {
            token.ThrowIfCancellationRequested();
            if (element.Bytes is { } bytes)
            {
                if (bytes.Length > syntax.Length - pending)
                {
                    await DrainAsync().ConfigureAwait(false);
                }

                if (bytes.Length >= syntax.Length)
                {
                    await output.WriteAsync(bytes.AsMemory(), token).ConfigureAwait(false);
                }
                else
                {
                    bytes.CopyTo(syntax, pending);
                    pending += bytes.Length;
                }
            }
            else
            {
                await DrainAsync().ConfigureAwait(false);
                await pdf.CopyStreamAsync(element.Stream!, output, token, transfer).ConfigureAwait(false);
            }
        }

        await DrainAsync().ConfigureAwait(false);
        Rendering.ConversionMetrics.Report("pdfSave", 1);

        async Task DrainAsync()
        {
            if (pending > 0)
            {
                await output.WriteAsync(syntax.AsMemory(0, pending), token).ConfigureAwait(false);
                pending = 0;
            }
        }
    }

    private static IEnumerable<OutputElement> Elements(GeneratedPdfFile pdf, IReadOnlyList<PageCorrection> pages, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pdf.Pages.Count != pages.Count)
        {
            throw new InvalidDataException("Skia PDF page count differs from the selected page plan.");
        }

        var revisions = new Dictionary<PdfReference, (PdfValue Value, GeneratedPdfFile.ObjectRecord? Stream)>();
        var next = pdf.Size;
        for (var index = 0; index < pages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var plan = pages[index];
            if (!double.IsFinite(plan.Width) || !double.IsFinite(plan.Height) || plan.Width <= 0 || plan.Height <= 0)
            {
                throw new InvalidDataException("Generated PDF page dimensions must be positive and finite.");
            }

            var pageReference = pdf.Pages[index];
            var page = pdf.Map(pageReference);
            var entries = new Dictionary<string, PdfValue>(page.Entries, StringComparer.Ordinal);
            var box = new PdfArray([new PdfNumber(0), new PdfNumber(0), new PdfNumber(Math.Round(plan.Width, 3, MidpointRounding.AwayFromZero)), new PdfNumber(Math.Round(plan.Height, 3, MidpointRounding.AwayFromZero))]);
            entries["/MediaBox"] = box;
            entries["/CropBox"] = box;

            // The previous save path established explicit device-RGB page compositing.
            // Retain that convention so transparency and antialiasing remain identical.
            if (!entries.ContainsKey("/Group"))
            {
                entries["/Group"] = new PdfMap(new() { ["/CS"] = new PdfName("/DeviceRGB"), ["/S"] = new PdfName("/Transparency") });
            }

            var originalAnnotations = pdf.Resolve(page.Get("/Annots"));
            if (originalAnnotations is not null && originalAnnotations is not PdfArray)
            {
                throw new InvalidDataException("Generated PDF annotations must be an array.");
            }

            // Native annotations are deliberately not emitted by the new writer. Any existing
            // entries must match the resolved link count before replacing that exact list.
            if (originalAnnotations is PdfArray existing && existing.Items.Count != plan.Links.Count)
            {
                throw new InvalidDataException("Unexpected generated PDF annotations.");
            }

            if (plan.Links.Count > 0)
            {
                var annotations = new List<PdfValue>();
                foreach (var link in plan.Links)
                {
                    var reference = new PdfReference(next++);
                    var bounds = link.Bounds;
                    var annotation = new Dictionary<string, PdfValue>(StringComparer.Ordinal)
                    {
                        ["/Type"] = new PdfName("/Annot"),
                        ["/Subtype"] = new PdfName("/Link"),
                        ["/Rect"] = new PdfArray([new PdfNumber(bounds.X), new PdfNumber(plan.Height - bounds.Y - bounds.Height), new PdfNumber(bounds.X + bounds.Width), new PdfNumber(plan.Height - bounds.Y)]),
                        ["/Border"] = new PdfArray([new PdfNumber(0), new PdfNumber(0), new PdfNumber(0)]),
                        ["/F"] = new PdfNumber(4),
                    };
                    if (link.Uri is { } uri)
                    {
                        annotation["/A"] = new PdfMap(new() { ["/S"] = new PdfName("/URI"), ["/URI"] = PdfString.Unicode(uri) });
                    }
                    else if (link.TargetPage is { } target && target >= 1 && target <= pages.Count && link.TargetX is { } x && link.TargetY is { } y)
                    {
                        annotation["/Dest"] = new PdfArray([pdf.Pages[target - 1], new PdfName("/XYZ"), new PdfNumber(x), new PdfNumber(pages[target - 1].Height - y), new PdfNumber(0)]);
                    }
                    else
                    {
                        throw new InvalidDataException("Generated PDF link has an invalid resolved destination.");
                    }

                    if (link.Tooltip is { } tooltip)
                    {
                        annotation["/Contents"] = PdfString.Unicode(new string(tooltip.Where(character => !char.IsControl(character)).Take(512).ToArray()));
                    }

                    revisions.Add(reference, (new PdfMap(annotation), null));
                    annotations.Add(reference);
                }

                entries["/Annots"] = new PdfArray(annotations);
            }

            revisions.Add(pageReference, (new PdfMap(entries), null));
        }

        var images = new List<GeneratedPdfFile.ObjectRecord>();
        var masks = new HashSet<PdfReference>();
        foreach (var reference in pdf.References)
        {
            token.ThrowIfCancellationRequested();
            var item = pdf.Read(reference);
            if (item.Value is PdfMap signed && (signed.Get("/Type") is PdfName { Value: "/Sig" } || signed.Get("/ByteRange") is not null))
            {
                throw new InvalidDataException("Signed PDFs are not generated-format inputs.");
            }

            if (item.Value is PdfMap dictionary && dictionary.Get("/Subtype") is PdfName { Value: "/Image" })
            {
                images.Add(item);
                if (dictionary.Get("/SMask") is PdfReference mask)
                {
                    masks.Add(mask);
                }
            }
        }

        foreach (var image in images.Where(image => !masks.Contains(image.Reference)))
        {
            var entries = new Dictionary<string, PdfValue>(((PdfMap)image.Value).Entries, StringComparer.Ordinal) { ["/Interpolate"] = new PdfLiteral("true") };

            if (((PdfMap)image.Value).Get("/Interpolate") is not PdfLiteral { Value: "true" })
            {
                revisions.Add(image.Reference, (new PdfMap(entries), image));
            }
        }

        long written = 0;
        OutputElement Text(string text)
        {
            token.ThrowIfCancellationRequested();
            var bytes = Encoding.GetEncoding(28591).GetBytes(text);
            written += bytes.Length;
            return new(bytes, null);
        }

        // Retain object identities and all active streams, but discard obsolete revisions.
        // The parser records stream ranges only; no payload is decoded or gathered in memory.
        yield return Text(pdf.Header + "\n%\u00e2\u00e3\u00cf\u00d3\n");
        var offsets = new List<(PdfReference Reference, long Offset)>();
        var references = pdf.References.Concat(revisions.Keys).Distinct().OrderBy(reference => reference.Number);
        foreach (var reference in references)
        {
            token.ThrowIfCancellationRequested();
            var original = reference.Number < pdf.Size ? pdf.Read(reference) : null;
            var value = revisions.TryGetValue(reference, out var revision) ? revision.Value : original!.Value;
            var stream = revision.Stream ?? original;
            offsets.Add((reference, written));
            yield return Text(reference.Number.ToString(CultureInfo.InvariantCulture) + " " + reference.Generation.ToString(CultureInfo.InvariantCulture) + " obj\n" + value.Serialize() + "\n");
            if (stream is { StreamOffset: > 0 })
            {
                yield return Text("stream\n");
                yield return new(null, stream);
                written += stream.StreamLength;
                yield return Text("\nendstream\n");
            }

            yield return Text("endobj\n");
        }

        var xref = written;
        yield return Text("xref\n0 1\n0000000000 65535 f \n");
        foreach (var item in offsets)
        {
            if (item.Offset > 9999999999)
            {
                throw new InvalidDataException("Generated PDF exceeds classic xref offset limits.");
            }

            yield return Text(string.Format(CultureInfo.InvariantCulture, "{0} 1\n{1:0000000000} {2:00000} n \n", item.Reference.Number, item.Offset, item.Reference.Generation));
        }

        var trailer = new Dictionary<string, PdfValue>(pdf.Trailer.Entries, StringComparer.Ordinal)
        {
            ["/Size"] = new PdfNumber(next),
        };
        trailer.Remove("/Prev");
        yield return Text("trailer\n" + new PdfMap(trailer).Serialize() + "\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
    }

    private readonly record struct OutputElement(byte[]? Bytes, GeneratedPdfFile.ObjectRecord? Stream);

    /// <summary>Stores exact final page dimensions and links independently of product models.</summary>
    /// <param name="Width">The width in points.</param>
    /// <param name="Height">The height in points.</param>
    /// <param name="Links">The final link list.</param>
    internal sealed record PageCorrection(double Width, double Height, IReadOnlyList<LinkCorrection> Links);

    /// <summary>Stores a resolved external URI or an internal final-page XYZ destination.</summary>
    /// <param name="Bounds">The top-origin link rectangle.</param>
    /// <param name="Uri">The external URI.</param>
    /// <param name="TargetPage">The one-based final output page.</param>
    /// <param name="TargetX">The target X coordinate.</param>
    /// <param name="TargetY">The target top-origin Y coordinate.</param>
    /// <param name="Tooltip">The optional tooltip.</param>
    internal sealed record LinkCorrection(ReportRect Bounds, string? Uri, int? TargetPage, double? TargetX, double? TargetY, string? Tooltip);
}
