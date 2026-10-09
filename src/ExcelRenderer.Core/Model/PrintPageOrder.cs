namespace ExcelRenderer.Core.Model;

/// <summary>Specifies how worksheet pages are ordered across horizontal and vertical bands.</summary>
internal enum PrintPageOrder
{
    /// <summary>Pages proceed down the sheet before moving right.</summary>
    DownThenOver,

    /// <summary>Pages proceed right across the sheet before moving down.</summary>
    OverThenDown,
}
