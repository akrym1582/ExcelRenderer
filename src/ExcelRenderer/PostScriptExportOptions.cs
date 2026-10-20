using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer;

/// <summary>Configures direct multipage PostScript export.</summary>
public sealed record PostScriptExportOptions
{
    /// <summary>Gets the worksheet name, or null for all worksheets.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the font selection policy.</summary>
    public FontOptions FontOptions { get; init; } = new();

    /// <summary>Gets the formula display policy.</summary>
    public ExcelReadOptions ReadOptions { get; init; } = new();

    /// <summary>Gets the bounded intermediate document spool policy.</summary>
    public RenderBufferOptions Buffering { get; init; } = new();
}
