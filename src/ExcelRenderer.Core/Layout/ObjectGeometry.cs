using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

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

    /// <summary>
    /// Gets the axis-aligned bounds of a rectangle rotated clockwise about its centre, matching the renderers.
    /// </summary>
    /// <param name="bounds">The unrotated rectangle.</param>
    /// <param name="rotation">The clockwise rotation in degrees.</param>
    /// <returns>The visual bounding rectangle.</returns>
    internal static ReportRect GetVisualBounds(ReportRect bounds, double rotation)
    {
        if (rotation == 0 || !double.IsFinite(rotation))
        {
            return bounds;
        }

        var radians = rotation * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var width = (bounds.Width * cos) + (bounds.Height * sin);
        var height = (bounds.Width * sin) + (bounds.Height * cos);
        var centerX = bounds.X + (bounds.Width / 2);
        var centerY = bounds.Y + (bounds.Height / 2);
        return new(centerX - (width / 2), centerY - (height / 2), width, height);
    }
}
