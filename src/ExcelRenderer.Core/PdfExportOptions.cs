namespace ExcelRenderer.Core;

/// <summary>Options for a PDF-only, single-font conversion.</summary>
public sealed record PdfExportOptions
{
    /// <summary>Gets the required path to a static TrueType font file or collection.</summary>
    public string FontFilePath { get; init; } = string.Empty;

    /// <summary>Gets the zero-based face index within the font collection; standalone fonts require zero.</summary>
    public int FontCollectionIndex { get; init; }

    /// <summary>Gets the optional sheet name; null selects every sheet in workbook order.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the reader engine and stored-formula display policy.</summary>
    public ExcelReadOptions ReadOptions { get; init; } = new();

    /// <summary>Gets the input spool and ZIP limits.</summary>
    public WorkbookInputOptions Input { get; init; } = new();
}
