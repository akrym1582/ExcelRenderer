using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

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
        var rect = ObjectGeometry.GetSheetRect(
            context.Geometry,
            fallbackAnchor,
            fallbackOffsetX,
            fallbackOffsetY,
            fallbackWidth,
            fallbackHeight,
            anchor);
        return TryResolve(context, rect, out bounds);
    }

    /// <summary>Maps an original neutral object rectangle to visible layout coordinates.</summary>
    /// <param name="context">The completed geometry.</param>
    /// <param name="rect">The original source rectangle.</param>
    /// <param name="bounds">The mapped rectangle.</param>
    /// <returns>Whether visible geometry exists.</returns>
    internal static bool TryResolve(ReportLayoutContext context, ReportRect rect, out ReportRect bounds)
    {
        if (context.VisibleColumns.Count == 0 || context.VisibleRows.Count == 0)
        {
            bounds = default;
            return false;
        }

        bounds = new(
            MapToLayout(rect.X, context.VisibleColumns, context.Geometry.ColumnStart, column => context.ColumnLayouts[column].X),
            MapToLayout(rect.Y, context.VisibleRows, context.Geometry.RowStart, row => context.RowLayouts[row].Y),
            rect.Width,
            rect.Height);
        return true;
    }

    /// <summary>
    /// Converts a sheet-origin coordinate to layout coordinates by subtracting the true origin of the
    /// nearest preceding visible column or row, so every anchor kind shares one origin.
    /// </summary>
    private static double MapToLayout(
        double position,
        IReadOnlyList<int> visible,
        Func<int, double> sheetStart,
        Func<int, double> layoutStart)
    {
        var reference = visible[0];
        foreach (var index in visible)
        {
            if (sheetStart(index) > position)
            {
                break;
            }

            reference = index;
        }

        return layoutStart(reference) + (position - sheetStart(reference));
    }
}
