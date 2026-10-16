using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Excel;

/// <summary>Holds basic workbook XML metadata and conversion-local typed enrichment.</summary>
internal sealed class CoreWorkbookMetadata
{
    /// <summary>Gets sparse cell diagnostics deferred until output selection.</summary>
    internal Dictionary<string, List<Rendering.ConversionDiagnostic>> CellDiagnostics { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets product-provided sheet placeholders for unsupported sheet kinds.</summary>
    internal List<ReportSheet> AdditionalSheets { get; } = [];

    /// <summary>Gets original workbook sheet positions when non-worksheet parts are present.</summary>
    internal Dictionary<string, int> SourceSheetIndices { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets additional neutral geometry before cell dimensions are materialized.</summary>
    internal Dictionary<string, IReadOnlyList<CellRange>> AdditionalGeometry { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether to retain geometry for sheets whose body is unselected.</summary>
    internal bool IncludeUnselectedSheetGeometry { get; set; }

    /// <summary>Gets or sets borrowed product metadata for this conversion.</summary>
    internal ICoreExtensionData? ExtensionData { get; set; }
}
