using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Preserves the legacy size-only measurer contract at the Core boundary.</summary>
internal class CoreTextMeasurerAdapter : Core.Abstractions.ITextMeasurer
{
    private readonly ITextMeasurer measurer;
    private readonly Dictionary<Core.Model.FontStyle, FontStyle> fonts = new();

    /// <summary>Initializes a new instance of the <see cref="CoreTextMeasurerAdapter"/> class.</summary>
    /// <param name="measurer">The public measurer.</param>
    internal CoreTextMeasurerAdapter(ITextMeasurer measurer) => this.measurer = measurer;

    /// <inheritdoc/>
    public Core.Abstractions.TextSize Measure(string text, Core.Model.FontStyle font, double availableWidth, bool wrap)
    {
        var size = measurer.Measure(text, ConvertFont(font), availableWidth, wrap);
        return new(size.Width, size.Height);
    }

    /// <summary>Selects the matching existing measurement contract.</summary>
    /// <param name="measurer">The public implementation.</param>
    /// <returns>The conversion-local adapter.</returns>
    internal static Core.Abstractions.ITextMeasurer Create(ITextMeasurer measurer) => measurer is ITextLayoutService layout ? new CoreTextLayoutAdapter(measurer, layout) : new CoreTextMeasurerAdapter(measurer);

    /// <summary>Converts each original font style once per adapter.</summary>
    /// <param name="font">The common font style.</param>
    /// <returns>The full font style.</returns>
    protected FontStyle ConvertFont(Core.Model.FontStyle font)
    {
        if (!fonts.TryGetValue(font, out var value))
        {
            value = CoreModelAdapter.ToPublic(font);
            fonts[font] = value;
        }

        return value;
    }
}
