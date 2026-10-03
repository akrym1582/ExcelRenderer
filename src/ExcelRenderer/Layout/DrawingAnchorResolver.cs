using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Resolves DrawingML anchors against the shared sheet geometry.</summary>
internal static class DrawingAnchorResolver
{
    /// <summary>Resolves an object's original point rectangle without a pixel round trip.</summary>
    /// <param name="context">The shared row and column geometry.</param>
    /// <param name="fallbackAnchor">The legacy anchor cell.</param>
    /// <param name="fallbackOffsetX">The legacy horizontal offset.</param>
    /// <param name="fallbackOffsetY">The legacy vertical offset.</param>
    /// <param name="fallbackWidth">The legacy object width.</param>
    /// <param name="fallbackHeight">The legacy object height.</param>
    /// <param name="anchor">The original DrawingML anchor, if available.</param>
    /// <param name="bounds">The resolved sheet-space rectangle.</param>
    /// <returns><see langword="true"/> when all required geometry exists.</returns>
    internal static bool TryResolve(
        ReportLayoutContext context,
        CellAddress fallbackAnchor,
        double fallbackOffsetX,
        double fallbackOffsetY,
        double fallbackWidth,
        double fallbackHeight,
        DrawingAnchor? anchor,
        out ReportRect bounds)
    {
        if (anchor?.Kind == DrawingAnchorKind.Absolute)
        {
            bounds = new(anchor.PositionX, anchor.PositionY, anchor.ExtentWidth, anchor.ExtentHeight);
            return true;
        }

        var from = anchor?.From ?? fallbackAnchor;
        if (!TryPosition(context, from, out var fromX, out var fromY))
        {
            bounds = default;
            return false;
        }

        fromX += anchor?.FromOffsetX ?? fallbackOffsetX;
        fromY += anchor?.FromOffsetY ?? fallbackOffsetY;
        if (anchor?.Kind == DrawingAnchorKind.TwoCell && anchor.To is { } to &&
            TryPosition(context, to, out var toX, out var toY))
        {
            bounds = new(
                fromX,
                fromY,
                Math.Max(0, toX + anchor.ToOffsetX - fromX),
                Math.Max(0, toY + anchor.ToOffsetY - fromY));
            return true;
        }

        var width = anchor is null ? fallbackWidth : anchor.ExtentWidth;
        var height = anchor is null ? fallbackHeight : anchor.ExtentHeight;
        bounds = new(fromX, fromY, width > 0 ? width : fallbackWidth, height > 0 ? height : fallbackHeight);
        return true;
    }

    private static bool TryPosition(ReportLayoutContext context, CellAddress address, out double x, out double y)
    {
        if (!context.ColumnLayouts.TryGetValue(address.Column, out var column) ||
            !context.RowLayouts.TryGetValue(address.Row, out var row))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = column.X;
        y = row.Y;
        return true;
    }
}
