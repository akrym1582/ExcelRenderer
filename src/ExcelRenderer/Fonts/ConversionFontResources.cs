using ExcelRenderer.Rendering;
using PdfSharp.Drawing;
using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>Owns native faces and PDF fonts only for the current conversion.</summary>
internal sealed class ConversionFontResources : IDisposable
{
    private static readonly AsyncLocal<ConversionFontResources?> CurrentSlot = new();
    private readonly ConversionFontResources? _previous;
    private readonly Dictionary<string, SKTypeface> _faces = new();
    private readonly Dictionary<(string Face, double Size), XFont> _pdfFonts = new();
    private readonly Dictionary<(string Face, double Size), (double Ascent, double Descent, double Leading)> _metrics = new();
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ConversionFontResources"/> class.</summary>
    internal ConversionFontResources()
    {
        _previous = CurrentSlot.Value;
        CurrentSlot.Value = this;
    }

    /// <summary>Gets the resource owner for the current conversion, if any.</summary>
    internal static ConversionFontResources? Current => CurrentSlot.Value;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CurrentSlot.Value = _previous;
        foreach (var face in _faces.Values)
        {
            face.Dispose();
            ConversionMetrics.Report("typefaceDisposed", 1);
        }

        if (_faces.Count > 0)
        {
            // Skia's process-wide strike cache can retain a disposed face's font data.
            // Purge unused strikes after our SKFont instances and typefaces are released;
            // strikes referenced by active drawing operations remain valid.
            SKGraphics.PurgeFontCache();
        }

        _faces.Clear();
        _pdfFonts.Clear();
        _metrics.Clear();
    }

    /// <summary>Gets one shared native face; callers must not dispose it.</summary>
    /// <param name="font">The resolved physical face.</param>
    /// <returns>The session-owned typeface.</returns>
    internal SKTypeface GetTypeface(ResolvedFont font)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConversionFontResources));
        }

        if (!_faces.TryGetValue(font.FaceId, out var face))
        {
            using Stream stream = font.FontData is null ? File.OpenRead(font.FilePath) : new MemoryStream(font.FontData, false);
            face = SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"フォント {font.Family} を読み込めません。");
            _faces.Add(font.FaceId, face);
            ConversionMetrics.Report("typefaceCreated", 1);
        }

        return face;
    }

    /// <summary>Gets a PDF font within the current PDFsharp reset generation.</summary>
    /// <param name="font">The resolved physical face.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>The shared PDF font.</returns>
    internal XFont GetPdfFont(ResolvedFont font, double size)
    {
        var key = (font.FaceId, size);
        if (!_pdfFonts.TryGetValue(key, out var result))
        {
            result = new XFont(PdfSharp.PdfSharpFontResolver.RegisterResolvedFont(font), size, XFontStyleEx.Regular);
            _pdfFonts.Add(key, result);
            ConversionMetrics.Report("xFontCreated", 1);
        }

        return result;
    }

    /// <summary>Gets native line metrics for one face and size.</summary>
    /// <param name="font">The resolved physical face.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>The ascent, descent and leading in points.</returns>
    internal (double Ascent, double Descent, double Leading) GetMetrics(ResolvedFont font, double size)
    {
        var key = (font.FaceId, size);
        if (!_metrics.TryGetValue(key, out var result))
        {
            using var skFont = new SKFont(GetTypeface(font), (float)size);
            var metrics = skFont.Metrics;
            result = (-metrics.Ascent, metrics.Descent, metrics.Leading);
            _metrics.Add(key, result);
            ConversionMetrics.Report("lineMetricsComputed", 1);
        }

        return result;
    }
}
