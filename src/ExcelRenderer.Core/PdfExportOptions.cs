namespace ExcelRenderer.Core;

/// <summary>Options for a PDF-only, single-font conversion.</summary>
public sealed record PdfExportOptions
{
    /// <summary>Gets the required path to a single static TrueType font file.</summary>
    public string FontFilePath { get; init; } = string.Empty;

    /// <summary>Gets the optional sheet name; null selects every sheet in workbook order.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the input spool and ZIP limits.</summary>
    public WorkbookInputOptions Input { get; init; } = new();
}
