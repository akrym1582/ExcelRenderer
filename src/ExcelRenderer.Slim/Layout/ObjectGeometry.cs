using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>Resolves drawing objects to sheet-space rectangles and their visual (rotated) bounds.</summary>
internal static class ObjectGeometry
{
    /// <summary>Resolves an object's unrotated rectangle in sheet-origin points.</summary>
    /// <param name="geometry">The print-area independent sheet geometry.</param>
    /// <param name="fallbackAnchor">The legacy anchor cell.</param>
    /// <param name="fallbackOffsetX">The legacy horizontal offset.</param>
    /// <param name="fallbackOffsetY">The legacy vertical offset.</param>
    /// <param name="fallbackWidth">The legacy width.</param>
    /// <param name="fallbackHeight">The legacy height.</param>
    /// <param name="anchor">The DrawingML anchor, if any.</param>
    /// <returns>The sheet-space rectangle.</returns>
    internal static ReportRect GetSheetRect(
        SheetGeometry geometry,
        CellAddress fallbackAnchor,
        double fallbackOffsetX,
        double fallbackOffsetY,
        double fallbackWidth,
        double fallbackHeight,
        DrawingAnchor? anchor)
    {
        if (anchor?.Kind == DrawingAnchorKind.Absolute)
        {
            return new(anchor.PositionX, anchor.PositionY, anchor.ExtentWidth, anchor.ExtentHeight);
        }

        var from = anchor?.From ?? fallbackAnchor;
        var x = geometry.ColumnStart(from.Column) + (anchor?.FromOffsetX ?? fallbackOffsetX);
        var y = geometry.RowStart(from.Row) + (anchor?.FromOffsetY ?? fallbackOffsetY);
        if (anchor?.Kind == DrawingAnchorKind.TwoCell && anchor.To is { } to)
        {
            return new(
                x,
                y,
                Math.Max(0, geometry.ColumnStart(to.Column) + anchor.ToOffsetX - x),
                Math.Max(0, geometry.RowStart(to.Row) + anchor.ToOffsetY - y));
        }

        var width = anchor is null ? fallbackWidth : anchor.ExtentWidth;
        var height = anchor is null ? fallbackHeight : anchor.ExtentHeight;
        return new(x, y, width > 0 ? width : fallbackWidth, height > 0 ? height : fallbackHeight);
    }
}
