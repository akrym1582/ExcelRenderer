using ClosedXML.Graphics;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using SkiaSharp;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Preserves full font discovery, compatibility fallback and MDW diagnostics.</summary>
internal sealed class FullFontMetrics : ICoreFontMetrics
{
    private readonly IFontManager? fontManager;
    private readonly DiagnosticCollector? diagnostics;
    private readonly Dictionary<NormalFontMetadata, double> maximumDigitWidths;

    /// <summary>Initializes a new instance of the <see cref="FullFontMetrics"/> class.</summary>
    /// <param name="fontManager">The optional full font manager.</param>
    /// <param name="diagnostics">The existing diagnostic collector.</param>
    /// <param name="maximumDigitWidths">The reader-local metric cache.</param>
    internal FullFontMetrics(IFontManager? fontManager, DiagnosticCollector? diagnostics, Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        this.fontManager = fontManager;
        this.diagnostics = diagnostics;
        this.maximumDigitWidths = maximumDigitWidths;
    }

    /// <inheritdoc/>
    public IXLGraphicEngine? GraphicEngine => null;

    /// <inheritdoc/>
    public NormalFontMetadata DefaultFont => new("Calibri", 11);

    /// <inheritdoc/>
    public double? DefaultColumnWidth(double width) => Math.Truncate((width * 7) + 5) * 72d / 96d;

    /// <inheritdoc/>
    public double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName)
    {
        if (fontManager is not null && normalFont is not null)
        {
            try
            {
                if (!maximumDigitWidths.TryGetValue(normalFont, out var cached))
                {
                    var resolved = fontManager.Resolve(new(normalFont.Family));
                    using Stream stream = resolved.FontData is null
                        ? File.OpenRead(resolved.FilePath)
                        : new MemoryStream(resolved.FontData, writable: false);
                    using var ownedTypeface = ConversionFontResources.Current is null ? SKTypeface.FromStream(stream) : null;
                    var typeface = ConversionFontResources.Current?.GetTypeface(resolved) ?? ownedTypeface ??
                        throw new InvalidOperationException($"Font {resolved.Family} could not be loaded.");
                    using var font = new SKFont(typeface, (float)(normalFont.Size * 96 / 72));
                    cached = Math.Max(
                        1,
                        Math.Round(
                        Enumerable.Range(0, 10).Max(digit => font.MeasureText(digit.ToString())),
                        MidpointRounding.AwayFromZero));
                    maximumDigitWidths[normalFont] = cached;
                    diagnostics?.Add(new(
                        "MaximumDigitWidthResolved",
                        DiagnosticSeverity.Info,
                        DiagnosticStage.Read,
                        $"Normal font '{normalFont.Family}' resolved to '{resolved.Family}' with MDW {cached}px.",
                        sheetName));
                }

                return cached;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            {
                // Fall through to the documented compatibility metric.
            }
        }

        diagnostics?.Add(new(
            "MaximumDigitWidthFallback",
            DiagnosticSeverity.Warning,
            DiagnosticStage.Read,
            $"Normal font '{normalFont?.Family ?? "unknown"}' could not be measured; the 7px compatibility metric was used.",
            sheetName));
        return 7;
    }
}
