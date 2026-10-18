using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Pdf;

/// <summary>The internal PDF boundary consumes final commands, dimensions, links, and page diagnostics.</summary>
internal interface IPdfDocumentWriter : IDisposable
{
    /// <summary>Gets a value indicating whether a font collection pass is required.</summary>
    bool RequiresFontCollection { get; }

    /// <summary>Collects document font usage without retaining page commands.</summary>
    /// <param name="commands">The final commands.</param>
    void Collect(IEnumerable<DrawCommand> commands);

    /// <summary>Prepares collected resources before drawing.</summary>
    void Prepare();

    /// <summary>Appends one page using finalized point geometry.</summary>
    /// <param name="settings">The page settings.</param>
    /// <param name="descriptor">The selected page metadata.</param>
    /// <param name="commands">The final commands.</param>
    /// <param name="links">The resolved links.</param>
    /// <param name="diagnostic">The page diagnostic callback.</param>
    void Append(PageSettings settings, RenderPageDescriptor descriptor, IEnumerable<DrawCommand> commands, IReadOnlyList<ResolvedPdfHyperlink> links, Action<ConversionDiagnostic> diagnostic);

    /// <summary>Completes the document and writes it to the caller-owned sink stream.</summary>
    /// <param name="output">The output stream.</param>
    void Complete(Stream output);
}
