using ExcelRenderer.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Corrects page boxes, link tooltips and image interpolation metadata without changing content streams.</summary>
internal static class SkiaPdfCompatibilityFinalizer
{
    /// <summary>Writes structural corrections without rendering any content through PDFsharp.</summary>
    /// <param name="source">The complete Skia PDF spool.</param>
    /// <param name="output">The caller-owned output.</param>
    /// <param name="pages">Exact dimensions and resolved annotations.</param>
    /// <param name="token">The conversion token.</param>
    internal static void Write(Stream source, Stream output, IReadOnlyList<(RenderPageDescriptor Descriptor, IReadOnlyList<ResolvedPdfHyperlink> Links)> pages, CancellationToken token)
    {
        source.Position = 0;
        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        if (document.PageCount != pages.Count)
        {
            throw new InvalidDataException("Skia PDF page count differs from the selected page plan.");
        }

        // Skia's PDF serializer omits /Interpolate even for linear/cubic sampling.
        // Match XImage's existing interpolation policy without resampling or
        // replacing image bytes. Soft-mask dictionaries retain their own policy.
        var images = document.Internals.GetAllObjects().OfType<PdfDictionary>()
            .Where(item => item.Elements.GetName("/Subtype") == "/Image").ToArray();
        var masks = new HashSet<PdfDictionary>(images.Select(image => image.Elements.GetDictionary("/SMask")).OfType<PdfDictionary>());
        foreach (var image in images.Where(image => !masks.Contains(image)))
        {
            token.ThrowIfCancellationRequested();
            image.Elements.SetBoolean("/Interpolate", true);
        }

        for (var index = 0; index < pages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var page = document.Pages[index];
            var (descriptor, links) = pages[index];
            var box = new PdfRectangle(new XPoint(0, 0), new XPoint(descriptor.WidthPoints, descriptor.HeightPoints));
            page.MediaBox = box;
            page.CropBox = box;
            if (page.Annotations.Count != links.Count)
            {
                throw new InvalidDataException("Skia PDF did not preserve every resolved link annotation.");
            }

            // Native annotations are matched by position, not by Skia's object or emission order.
            var unmatched = new HashSet<int>(Enumerable.Range(0, page.Annotations.Count));
            foreach (var link in links)
            {
                var expectedX = link.Bounds.X;
                var expectedY = descriptor.HeightPoints - link.Bounds.Y - link.Bounds.Height;
                var match = unmatched.Where(candidate =>
                    Math.Abs(page.Annotations[candidate].Rectangle.X1 - expectedX) < .1 &&
                    Math.Abs(page.Annotations[candidate].Rectangle.Y1 - expectedY) < .1 &&
                    Math.Abs(page.Annotations[candidate].Rectangle.Width - link.Bounds.Width) < .1 &&
                    Math.Abs(page.Annotations[candidate].Rectangle.Height - link.Bounds.Height) < .1 &&
                    (link.Uri is null || page.Annotations[candidate].Elements.GetDictionary("/A")?.Elements.GetString("/URI") == link.Uri)).DefaultIfEmpty(-1).First();
                if (match < 0)
                {
                    throw new InvalidDataException("Skia PDF link annotation coordinates differ from the resolved plan.");
                }

                unmatched.Remove(match);
                if (link.Tooltip is { } tooltip)
                {
                    page.Annotations[match].Elements.SetString("/Contents", new string(tooltip.Where(character => !char.IsControl(character)).Take(512).ToArray()));
                }
            }
        }

        token.ThrowIfCancellationRequested();
        Core.Pdf.CorePdfRenderer.Save(document, output);
    }
}
