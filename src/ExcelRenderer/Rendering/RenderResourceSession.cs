using ExcelRenderer.Fonts;

namespace ExcelRenderer.Rendering;

/// <summary>Owns conversion resources and the bounded text cache shared by both page passes.</summary>
internal sealed class RenderResourceSession : IDisposable
{
    private static readonly AsyncLocal<RenderResourceSession?> CurrentSlot = new();
    private readonly RenderResourceSession? previous;
    private readonly ConversionFontResources fonts;
    private readonly ImageResources images = new();
    private bool disposed;
    private int activePayloads;
    private int maximumPayloads;

    /// <summary>Initializes a new instance of the <see cref="RenderResourceSession"/> class.</summary>
    /// <param name="token">The conversion cancellation token.</param>
    internal RenderResourceSession(CancellationToken token = default)
    {
        fonts = new(token);
        previous = CurrentSlot.Value;
        CurrentSlot.Value = this;
    }

    /// <summary>Gets the resource owner in the current conversion.</summary>
    internal static RenderResourceSession? Current => CurrentSlot.Value;

    /// <summary>Gets the clock snapshot shared by header/footer generation in both page passes.</summary>
    internal DateTime HeaderFooterTimestamp { get; } = Pdf.PdfRenderObserver.Timestamp ?? DateTime.Now;

    /// <summary>Gets or sets the conversion-local bounded text layout cache.</summary>
    internal Abstractions.ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Gets or sets the bounded native compatibility layout service shared by image passes.</summary>
    internal Abstractions.ITextLayoutService? RasterTextLayout { get; set; }

    /// <summary>Gets or sets the built-in run completion service shared by vector passes.</summary>
    internal Abstractions.ITextLayoutService? ResolvedTextLayout { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        TextMeasurer = null;
        RasterTextLayout = null;
        ResolvedTextLayout = null;
        try
        {
            images.Dispose();
        }
        finally
        {
            fonts.Dispose();
            CurrentSlot.Value = previous;
        }
    }

    /// <summary>Records payload ownership without retaining the payload or its commands.</summary>
    /// <param name="delta">One on entry and minus one on exit.</param>
    internal void RecordPayload(int delta)
    {
        activePayloads += delta;
        if (activePayloads > maximumPayloads)
        {
            maximumPayloads = activePayloads;
            ConversionMetrics.Report("pagePayloadMax", maximumPayloads);
        }
    }
}
