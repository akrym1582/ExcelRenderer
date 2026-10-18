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
    private readonly SpillableBufferStream _spool;
    private readonly SkiaPdfFonts _fonts;
    private readonly SkiaDrawingContext _drawing;
    private readonly CancellationToken _token;
    private readonly List<(RenderPageDescriptor Descriptor, IReadOnlyList<ResolvedPdfHyperlink> Links)> _pages = new();
    private readonly HashSet<(int Page, double X, double Y, string Name)> _destinations = new();
    private SKDocument? _document;
    private SKManagedWStream? _stream;
    private bool _closed;

    /// <summary>Initializes a new instance of the <see cref="SkiaPdfDocumentWriter"/> class.</summary>
    /// <param name="fonts">The conversion font manager.</param>
    /// <param name="token">The conversion token.</param>
    /// <param name="fullEmbed">Internal comparison-only full embedding control.</param>
    /// <param name="buffering">The intermediate output spool settings.</param>
    internal SkiaPdfDocumentWriter(IFontManager fonts, CancellationToken token, bool fullEmbed = false, RenderBufferOptions? buffering = null)
    {
        _spool = new(buffering ?? new());
        _token = token;
        _fonts = new(token, fullEmbed);
        _drawing = new(false, fonts, _fonts);
    }

    /// <inheritdoc/>
    public bool RequiresFontCollection => true;

    /// <summary>Gets or sets the internal comparison observer before structural finalization.</summary>
    internal Action<Stream>? BeforeFinalize { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (!_closed)
            {
                _document?.Abort();
            }
        }
        finally
        {
            _document?.Dispose();
            _stream?.Dispose();
            _fonts.Dispose();
            _spool.Dispose();
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
        _stream = new SKManagedWStream(_spool, false);
        _document = SKDocument.CreatePdf(_stream) ?? throw new InvalidOperationException("Skia could not create a PDF document.");
    }

    /// <inheritdoc/>
    public void Append(PageSettings settings, RenderPageDescriptor descriptor, IEnumerable<DrawCommand> commands, IReadOnlyList<ResolvedPdfHyperlink> links, Action<ConversionDiagnostic> diagnostic)
    {
        _token.ThrowIfCancellationRequested();
        _drawing.DiagnosticHandler = diagnostic;
        var deviceWidth = Math.Ceiling(descriptor.WidthPoints);
        var deviceHeight = Math.Ceiling(descriptor.HeightPoints);
        var canvas = _document!.BeginPage((float)deviceWidth, (float)deviceHeight);

        // Skia rounds its PDF device extent to integer points. Allocate the enclosing
        // integer extent so fractional right/bottom strips are never lost, then retain
        // the exact top origin and clip. The finalizer restores boxes without scaling.
        canvas.Translate(0, (float)(deviceHeight - descriptor.HeightPoints));
        canvas.ClipRect(new(0, 0, (float)descriptor.WidthPoints, (float)descriptor.HeightPoints));
        foreach (var command in commands)
        {
            _token.ThrowIfCancellationRequested();
            _drawing.Execute(canvas, command);
        }

        // A destination at every source link's target is emitted on the target page. Forward
        // references are supported by Skia and do not require drawing the target page first.
        foreach (var target in _destinations.Where(target => target.Page == descriptor.OutputPageNumber))
        {
            canvas.DrawNamedDestinationAnnotation(new((float)target.X, (float)target.Y), target.Name);
        }

        foreach (var link in links)
        {
            var bounds = link.Bounds;
            var rect = new SKRect((float)bounds.X, (float)bounds.Y, (float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height));
            if (link.Uri is { } uri)
            {
                canvas.DrawUrlAnnotation(rect, uri);
            }
            else
            {
                canvas.DrawLinkDestinationAnnotation(rect, DestinationName(link.TargetPage!.Value, link.TargetX!.Value, link.TargetY!.Value));
            }
        }

        _document.EndPage();
        _pages.Add((descriptor, links));
    }

    /// <inheritdoc/>
    public void Complete(Stream output)
    {
        _token.ThrowIfCancellationRequested();
        _document!.Close();
        _closed = true;
        _stream!.Flush();
        _spool.Position = 0;
        BeforeFinalize?.Invoke(_spool);
        SkiaPdfCompatibilityFinalizer.Write(_spool, output, _pages, _token);
    }

    /// <summary>Registers resolved targets before any page is drawn.</summary>
    /// <param name="links">All selected page links.</param>
    internal void RegisterDestinations(IEnumerable<ResolvedPdfHyperlink> links)
    {
        foreach (var link in links.Where(link => link.TargetPage is not null))
        {
            _destinations.Add((link.TargetPage!.Value, link.TargetX!.Value, link.TargetY!.Value, DestinationName(link.TargetPage.Value, link.TargetX.Value, link.TargetY.Value)));
        }
    }

    private static string DestinationName(int page, double x, double y) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "er-page-{0}-x-{1:R}-y-{2:R}", page, x, y);
}
