namespace ExcelRenderer.Excel;

/// <summary>Chooses the font metrics used to interpret OOXML column widths.</summary>
public enum ColumnWidthMetricsMode
{
    /// <summary>Measure the original Normal font, independently of drawing substitutions.</summary>
    SourceFont,

    /// <summary>Use the historical drawing-font resolution for compatibility.</summary>
    RenderingFontLegacy,

    /// <summary>Use substitute-font digit metrics and fractional column widths for LibreOffice compatibility.</summary>
    LibreOfficeCompatible,

    /// <summary>Use fallback metrics without matching or discovering the original Normal font.</summary>
    Fallback,
}
