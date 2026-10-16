namespace ExcelRenderer.OpenXml;

/// <summary>Stored cell value categories for adapter dispatch and style cache keys.</summary>
public enum CellValueType
{
    /// <summary>No stored value.</summary>
    Blank,

    /// <summary>Decoded shared, inline or ordinary text.</summary>
    String,

    /// <summary>A finite numeric serial or scalar.</summary>
    Number,

    /// <summary>A stored boolean.</summary>
    Boolean,

    /// <summary>An Excel error token.</summary>
    Error,

    /// <summary>An ISO date string without timezone conversion.</summary>
    IsoDate,
}
