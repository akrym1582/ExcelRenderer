namespace ExcelRenderer.Slim.Model;

/// <summary>Identifies the DrawingML anchor representation stored by the workbook.</summary>
internal enum DrawingAnchorKind
{
    /// <summary>A two-cell anchor with from and to markers.</summary>
    TwoCell,

    /// <summary>A one-cell anchor with a from marker and extent.</summary>
    OneCell,

    /// <summary>An absolute anchor with a position and extent.</summary>
    Absolute,
}

/// <summary>Preserves DrawingML anchor coordinates in points without a pixel round-trip.</summary>
/// <param name="Kind">Stored anchor representation.</param>
/// <param name="From">Top-left marker cell.</param>
/// <param name="FromOffsetX">Horizontal from-marker offset in points.</param>
/// <param name="FromOffsetY">Vertical from-marker offset in points.</param>
/// <param name="To">Bottom-right marker cell for a two-cell anchor.</param>
/// <param name="ToOffsetX">Horizontal to-marker offset in points.</param>
/// <param name="ToOffsetY">Vertical to-marker offset in points.</param>
/// <param name="PositionX">Absolute horizontal position in points.</param>
/// <param name="PositionY">Absolute vertical position in points.</param>
/// <param name="ExtentWidth">Stored extent width in points.</param>
/// <param name="ExtentHeight">Stored extent height in points.</param>
/// <param name="EditAs">Raw two-cell editAs behavior.</param>
internal sealed record DrawingAnchor(
    DrawingAnchorKind Kind,
    CellAddress? From,
    double FromOffsetX,
    double FromOffsetY,
    CellAddress? To,
    double ToOffsetX,
    double ToOffsetY,
    double PositionX,
    double PositionY,
    double ExtentWidth,
    double ExtentHeight,
    string? EditAs = null);
