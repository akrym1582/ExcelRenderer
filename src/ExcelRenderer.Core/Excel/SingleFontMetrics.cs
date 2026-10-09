using ClosedXML.Graphics;
using ExcelRenderer.Core.Fonts;

namespace ExcelRenderer.Core.Excel;

/// <summary>Uses only the caller-supplied regular font for Core workbook metrics.</summary>
internal sealed class SingleFontMetrics : ICoreFontMetrics
{
    private readonly SingleFontContext context;
    private readonly Dictionary<NormalFontMetadata, double> widths = new();

    /// <summary>Initializes a new instance of the <see cref="SingleFontMetrics"/> class.</summary>
    /// <param name="context">The conversion-owned font.</param>
    internal SingleFontMetrics(SingleFontContext context)
    {
        this.context = context;
        GraphicEngine = new SingleFontGraphicEngine(context);
    }

    /// <inheritdoc/>
    public IXLGraphicEngine GraphicEngine { get; }

    /// <inheritdoc/>
    public NormalFontMetadata DefaultFont => new(SingleFontContext.Family, 11);

    /// <inheritdoc/>
    public double MaximumDigitWidth(NormalFontMetadata? normalFont, string sheetName)
    {
        var key = normalFont ?? DefaultFont;
        if (!widths.TryGetValue(key, out var width))
        {
            width = context.MaximumDigitWidth(key.Size);
            widths[key] = width;
        }

        return width;
    }

    /// <inheritdoc/>
    public double? DefaultColumnWidth(double width) => null;
}
