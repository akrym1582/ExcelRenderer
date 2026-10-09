using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Supplies a size-only placeholder for geometry-only public pass boundaries.</summary>
internal sealed class RangeOnlyMeasurer : ITextMeasurer
{
    /// <inheritdoc/>
    public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => default;
}
