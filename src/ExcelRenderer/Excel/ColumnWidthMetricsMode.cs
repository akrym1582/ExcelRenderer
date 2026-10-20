namespace ExcelRenderer.Excel;

/// <summary>Chooses the font metrics used to interpret OOXML column widths.</summary>
public enum ColumnWidthMetricsMode
{
    /// <summary>Measure the original Normal font, independently of drawing substitutions.</summary>
    SourceFont,

    /// <summary>Use the historical drawing-font resolution for compatibility.</summary>
    RenderingFontLegacy,
}
