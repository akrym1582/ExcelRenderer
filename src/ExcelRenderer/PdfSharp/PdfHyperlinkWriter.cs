using ExcelRenderer.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ExcelRenderer.PdfSharp;

/// <summary>Adds annotations only after all pages belong to the final document.</summary>
internal static class PdfHyperlinkWriter
{
    /// <summary>Writes final-page annotations using PDF default coordinates.</summary>
    /// <param name="document">The final output document.</param>
    /// <param name="pageNumber">The final one-based source page.</param>
    /// <param name="height">The final source page height.</param>
    /// <param name="links">Resolved link metadata.</param>
    internal static void Write(PdfDocument document, int pageNumber, double height, IReadOnlyList<ResolvedPdfHyperlink> links)
    {
        var page = document.Pages[pageNumber - 1];
        foreach (var link in links)
        {
            var b = link.Bounds;
            var rect = new PdfRectangle(new XPoint(b.X, height - b.Y - b.Height), new XPoint(b.X + b.Width, height - b.Y));
            var annotation = link.Uri is { } uri ? page.AddWebLink(rect, uri) :
                page.AddDocumentLink(rect, link.TargetPage!.Value, new XPoint(link.TargetX!.Value, link.TargetHeight!.Value - link.TargetY!.Value));
            annotation.Elements["/Border"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(0));
            if (link.Tooltip is { } tooltip)
            {
                annotation.Elements.SetString("/Contents", new string(tooltip.Where(c => !char.IsControl(c)).Take(512).ToArray()));
            }
        }
    }
}
