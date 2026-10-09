using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Excel;

/// <summary>Holds basic workbook XML metadata and conversion-local typed enrichment.</summary>
internal sealed class CoreWorkbookMetadata
{
    /// <summary>Initializes a new instance of the <see cref="CoreWorkbookMetadata"/> class.</summary>
    /// <param name="pictures">The single basic picture metadata read.</param>
    /// <param name="pageSetups">The single basic layout metadata read.</param>
    internal CoreWorkbookMetadata(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, DrawingPictureMetadata>> pictures,
        IReadOnlyDictionary<string, SheetPageSetupMetadata> pageSetups)
    {
        Pictures = pictures;
        PageSetups = pageSetups;
    }

    /// <summary>Gets picture anchors and drawing order.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, DrawingPictureMetadata>> Pictures { get; }

    /// <summary>Gets workbook layout metadata.</summary>
    internal IReadOnlyDictionary<string, SheetPageSetupMetadata> PageSetups { get; }

    /// <summary>Gets additional neutral geometry before cell dimensions are materialized.</summary>
    internal Dictionary<string, IReadOnlyList<CellRange>> AdditionalGeometry { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether to retain geometry for sheets whose body is unselected.</summary>
    internal bool IncludeUnselectedSheetGeometry { get; set; }

    /// <summary>Gets or sets borrowed product metadata for this conversion.</summary>
    internal ICoreExtensionData? ExtensionData { get; set; }
}
