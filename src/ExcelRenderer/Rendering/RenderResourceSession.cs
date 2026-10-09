using ExcelRenderer.Fonts;
using ExcelRenderer.PdfSharp;

namespace ExcelRenderer.Rendering;

/// <summary>Owns conversion resources and the bounded text cache shared by both page passes.</summary>
internal sealed class RenderResourceSession : IDisposable
{
    private static readonly AsyncLocal<RenderResourceSession?> CurrentSlot = new();
    private readonly RenderResourceSession? previous;
    private readonly ConversionFontResources fonts = new();
    private readonly ImageResources images = new();
    private bool disposed;
    private int activePayloads;
    private int maximumPayloads;

    /// <summary>Initializes a new instance of the <see cref="RenderResourceSession"/> class.</summary>
    internal RenderResourceSession()
    {
        previous = CurrentSlot.Value;
        CurrentSlot.Value = this;
    }

    /// <summary>Gets the resource owner in the current conversion.</summary>
    internal static RenderResourceSession? Current => CurrentSlot.Value;

    /// <summary>Gets the clock snapshot shared by header/footer generation in both page passes.</summary>
    internal DateTime HeaderFooterTimestamp { get; } = DateTime.Now;

    /// <summary>Gets or sets the borrowed font session owned by the converter.</summary>
    internal Core.Rendering.PdfSharpFontGate.Lease? FontSession { get; set; }

    /// <summary>Gets or sets the conversion-local bounded text layout cache.</summary>
    internal PdfSharpTextMeasurer? TextMeasurer { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        TextMeasurer = null;
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
