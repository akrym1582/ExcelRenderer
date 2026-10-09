using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Preserves full metadata without putting feature interpretation in Core.</summary>
/// <param name="Shapes">Selected-sheet shapes.</param>
/// <param name="Pictures">Picture transformations.</param>
/// <param name="Hyperlinks">Hyperlink display and target metadata.</param>
internal sealed record FullWorkbookData(
    IReadOnlyDictionary<string, IReadOnlyList<ReportShape>> Shapes,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, DrawingPictureMetadata>> Pictures,
    IReadOnlyDictionary<string, SheetHyperlinkMetadata> Hyperlinks) : ICoreExtensionData;
