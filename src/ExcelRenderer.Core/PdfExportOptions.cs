using ExcelRenderer.OpenXml.Reader.Input;

namespace ExcelRenderer.Core;

/// <summary>Options for a PDF-only, single-font conversion.</summary>
public sealed record PdfExportOptions
{
    /// <summary>Gets the required path to a static TrueType font file or collection.</summary>
    public string FontFilePath { get; init; } = string.Empty;

    /// <summary>Gets the zero-based face index within the font collection; standalone fonts require zero.</summary>
    public int FontCollectionIndex { get; init; }

    /// <summary>Gets one worksheet name. Use SheetNames for multiple sheets; leave both null to export all worksheets.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the worksheet names in output order, with duplicates removed. Null uses SheetName or all worksheets. Cannot be combined with SheetName.</summary>
    public IReadOnlyList<string>? SheetNames { get; init; }

    /// <summary>Gets the reader engine and stored-formula display policy.</summary>
    public ExcelReadOptions ReadOptions { get; init; } = new();

    /// <summary>Gets the Reader input spool and ZIP limits.</summary>
    public WorkbookInputOptions Input { get; init; } = new();
}
