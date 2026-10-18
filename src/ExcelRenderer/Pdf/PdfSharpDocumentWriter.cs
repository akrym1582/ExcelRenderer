using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using PdfSharp.Pdf;

namespace ExcelRenderer.Pdf;

/// <summary>Preserves the existing PDFsharp drawing and shared Core save path.</summary>
internal sealed class PdfSharpDocumentWriter(IFontManager fonts) : IPdfDocumentWriter
{
    private readonly PdfDocument _document = new();
    private readonly PdfSharpRenderer _renderer = new(fonts);
    private readonly List<Action> _links = new();

    /// <inheritdoc/>
    public bool RequiresFontCollection => false;

    /// <inheritdoc/>
    public void Dispose() => _document.Dispose();

    /// <inheritdoc/>
    public void Collect(IEnumerable<DrawCommand> commands)
    {
    }

    /// <inheritdoc/>
    public void Prepare()
    {
    }

    /// <inheritdoc/>
    public void Append(PageSettings settings, RenderPageDescriptor descriptor, IEnumerable<DrawCommand> commands, IReadOnlyList<ResolvedPdfHyperlink> links, Action<ConversionDiagnostic> diagnostic)
    {
        _renderer.PageCompleted = _ => _links.Add(() => PdfHyperlinkWriter.Write(_document, descriptor.OutputPageNumber!.Value, descriptor.HeightPoints, links));
        _renderer.PageDiagnosticHandler = diagnostic;
        _renderer.AppendPage(_document, settings, commands);
    }

    /// <inheritdoc/>
    public void Complete(Stream output)
    {
        foreach (var complete in _links)
        {
            complete();
        }

        Core.Pdf.CorePdfRenderer.Save(_document, output);
    }
}
