using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Excel;

/// <summary>Resolves source column metrics separately from single-font drawing substitutions.</summary>
internal sealed class SingleFontMetrics : ICoreFontMetrics
{
    private readonly SingleFontContext context;
    private readonly DiagnosticCollector? diagnostics;
    private readonly Dictionary<(NormalFontMetadata Font, bool SourceMatches), double> widths = new();

    /// <summary>Initializes a new instance of the <see cref="SingleFontMetrics"/> class.</summary>
    /// <param name="context">The conversion-owned font.</param>
    /// <param name="diagnostics">The conversion diagnostic collector.</param>
    internal SingleFontMetrics(SingleFontContext context, DiagnosticCollector? diagnostics = null)
    {
        this.context = context;
        this.diagnostics = diagnostics;
    }

    /// <inheritdoc/>
    public NormalFontMetadata DefaultFont => new("Calibri", 11);

    /// <inheritdoc/>
    public double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName)
    {
        var key = normalFont ?? DefaultFont;
        var sourceMatches = normalFont is not null && string.Equals(context.Face.Typeface.FamilyName, key.Family, StringComparison.OrdinalIgnoreCase);
        if (!widths.TryGetValue((key, sourceMatches), out var width))
        {
            width = sourceMatches ? context.MaximumDigitWidth(key.Size) : 7;
            widths[(key, sourceMatches)] = width;
        }

        if (!sourceMatches)
        {
            diagnostics?.Add(new(
                "MaximumDigitWidthFallback",
                DiagnosticSeverity.Warning,
                DiagnosticStage.Read,
                $"Normal '{normalFont?.Family ?? "unknown"}', {key.Size}pt; supplied drawing font '{context.Face.Typeface.FamilyName}' is not the source Normal font. Column layout uses the 7px compatibility metric at 96 DPI; exact Excel layout cannot be guaranteed.",
                sheetName));
        }

        return width;
    }
}
