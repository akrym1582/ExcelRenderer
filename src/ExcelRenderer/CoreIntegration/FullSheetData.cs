using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Retains full sheet features alongside the common body model.</summary>
/// <param name="Shapes">Selected-sheet shapes.</param>
/// <param name="Hyperlinks">Original hyperlink definitions and display metadata.</param>
internal sealed record FullSheetData(IReadOnlyList<ReportShape> Shapes, SheetHyperlinkMetadata? Hyperlinks) : ICoreExtensionData
{
    /// <summary>Gets finite object bounds for durable public Reader dimension dictionaries.</summary>
    internal IReadOnlyList<Core.Model.CellRange> DimensionRanges { get; init; } = [];

    /// <summary>Gets deferred formula display diagnostics.</summary>
    internal IReadOnlyList<Rendering.ConversionDiagnostic> CellReadDiagnostics { get; init; } = [];

    /// <summary>Gets deferred unsupported-feature candidates for selected output.</summary>
    internal IReadOnlyList<UnsupportedFeature> UnsupportedFeatures { get; init; } = [];
}
