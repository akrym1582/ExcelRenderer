namespace ExcelRenderer.Model;

/// <summary>Specifies how worksheet pages are ordered across horizontal and vertical bands.</summary>
public enum PrintPageOrder
{
    /// <summary>Pages proceed down the sheet before moving right.</summary>
    DownThenOver,

    /// <summary>Pages proceed right across the sheet before moving down.</summary>
    OverThenDown,
}
