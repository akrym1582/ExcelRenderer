namespace ExcelRenderer.Core;

/// <summary>Controls saved-value projection of workbook cells.</summary>
public sealed record ExcelReadOptions
{
    /// <summary>Gets the conversion's explicit finite source region for default-style materialization.</summary>
    internal Func<string, (int FirstRow, int FirstColumn, int LastRow, int LastColumn)?>? GetSelectedRange { get; init; }
}
