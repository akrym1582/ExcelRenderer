using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using ExcelRenderer.Rendering;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Owns one native PDF document and bounded spool, sharing subset faces across pages.</summary>
internal sealed class SkiaPdfDocumentWriter : IPdfDocumentWriter
{
    private readonly Core.Pdf.SkiaPdfDocument document;
    private readonly SkiaPdfFonts _fonts;
    private readonly SkiaDrawingContext _drawing;
    private readonly CancellationToken _token;

    /// <summary>Initializes a new instance of the <see cref="SkiaPdfDocumentWriter"/> class.</summary>
    /// <param name="fonts">The conversion font manager.</param>
    /// <param name="token">The conversion token.</param>
    /// <param name="fullEmbed">Internal comparison-only full embedding control.</param>
    /// <param name="buffering">The intermediate output spool settings.</param>
    internal SkiaPdfDocumentWriter(IFontManager fonts, CancellationToken token, bool fullEmbed = false, RenderBufferOptions? buffering = null)
    {
        buffering ??= new();
        document = new(
            new Core.Input.InputBufferOptions
            {
                MemoryThresholdBytes = buffering.MemoryThresholdBytes,
                AllowTemporaryFiles = buffering.AllowTemporaryFiles,
                TemporaryDirectory = buffering.TemporaryDirectory,
            },
            token);
        document.BeforeFinalize = source => PdfRenderObserver.NativePdf?.Invoke(source);
        _token = token;
        _fonts = new(token, fullEmbed);
        _drawing = new(false, fonts, _fonts);
    }

    /// <inheritdoc/>
    public bool RequiresFontCollection => true;

    /// <summary>Gets or sets the internal comparison observer before structural finalization.</summary>
    internal Action<Stream>? BeforeFinalize { get => document.BeforeFinalize; set => document.BeforeFinalize = value; }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            document.Dispose();
        }
        finally
        {
            _fonts.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Collect(IEnumerable<DrawCommand> commands)
    {
        using var surface = SKSurface.CreateNull(1, 1);
        foreach (var command in commands)
        {
            _token.ThrowIfCancellationRequested();
            _drawing.Execute(surface.Canvas, command);
        }
    }

    /// <inheritdoc/>
    public void Prepare()
    {
        _fonts.Prepare();
        document.Prepare();
    }

    /// <inheritdoc/>
    public void Append(PageSettings settings, RenderPageDescriptor descriptor, IEnumerable<DrawCommand> commands, IReadOnlyList<ResolvedPdfHyperlink> links, Action<ConversionDiagnostic> diagnostic)
    {
        _token.ThrowIfCancellationRequested();
        _drawing.DiagnosticHandler = diagnostic;
        var page = new Core.Pdf.GeneratedPdfFinalizer.PageCorrection(
            descriptor.WidthPoints,
            descriptor.HeightPoints,
            links.Select(link => new Core.Pdf.GeneratedPdfFinalizer.LinkCorrection(CoreIntegration.CoreModelAdapter.ToCore(link.Bounds), link.Uri, link.TargetPage, link.TargetX, link.TargetY, link.Tooltip)).ToArray());
        document.Append(page, canvas =>
        {
            foreach (var command in commands)
            {
                _token.ThrowIfCancellationRequested();
                _drawing.Execute(canvas, command);
            }
        });
    }

    /// <inheritdoc/>
    public void Complete(Stream output) => document.Complete(output);
}
