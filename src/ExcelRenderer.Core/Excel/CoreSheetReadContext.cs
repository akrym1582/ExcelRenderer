namespace ExcelRenderer.Core.Excel;

/// <summary>Identifies the original worksheet and its body projection.</summary>
/// <param name="Metadata">The enriched workbook metadata.</param>
/// <param name="SourceSheetIndex">The original one-based workbook index.</param>
/// <param name="IncludeBody">Whether to read cells and image bytes.</param>
internal sealed record CoreSheetReadContext(CoreWorkbookMetadata Metadata, int SourceSheetIndex, bool IncludeBody)
{
    /// <summary>Gets the original worksheet name.</summary>
    internal string SheetName { get; init; } = string.Empty;

    /// <summary>Gets source conditional rule ranges captured before body traversal.</summary>
    internal IReadOnlyList<IReadOnlyList<Model.CellRange>> ConditionalFormattingRanges { get; init; } = [];
}
