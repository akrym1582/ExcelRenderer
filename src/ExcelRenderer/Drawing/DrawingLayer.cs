namespace ExcelRenderer.Drawing;

/// <summary>Identifies a cell drawing layer; order is specified by the command generator.</summary>
internal enum DrawingLayer
{
    /// <summary>Cell background fills.</summary>
    Background,

    /// <summary>Cell borders.</summary>
    CellBorder,

    /// <summary>Merged-cell perimeter fragments.</summary>
    MergedBorder,

    /// <summary>Cell text, drawn after fills and borders.</summary>
    Text,
}
