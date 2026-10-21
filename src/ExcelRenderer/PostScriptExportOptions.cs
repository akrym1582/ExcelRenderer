using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer;

/// <summary>Configures direct multipage PostScript export.</summary>
public sealed record PostScriptExportOptions
{
    /// <summary>Gets one worksheet name. Use SheetNames for multiple sheets; leave both null to export all worksheets.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the worksheet names in output order, with duplicates removed. Null uses SheetName or all worksheets. Cannot be combined with SheetName.</summary>
    public IReadOnlyList<string>? SheetNames { get; init; }

    /// <summary>Gets the font selection policy.</summary>
    public FontOptions FontOptions { get; init; } = new();

    /// <summary>Gets the formula display policy.</summary>
    public ExcelReadOptions ReadOptions { get; init; } = new();

    /// <summary>Gets the bounded intermediate document spool policy.</summary>
    public RenderBufferOptions Buffering { get; init; } = new();
}
