using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

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
        => CoreIntegration.CoreModelAdapter.ToPublic(Core.Layout.ObjectGeometry.GetSheetRect(
            geometry.CoreGeometry,
            CoreIntegration.CoreModelAdapter.ToCore(fallbackAnchor),
            fallbackOffsetX,
            fallbackOffsetY,
            fallbackWidth,
            fallbackHeight,
            anchor is null ? null : CoreIntegration.CoreModelAdapter.ToCore(anchor)));

    /// <summary>Gets conservative rotated bounds including a shape's protrusion and visible stroke.</summary>
    /// <param name="bounds">The original shape rectangle.</param>
    /// <param name="shape">The shape style and geometry.</param>
    /// <returns>The visual sheet-space bounding rectangle.</returns>
    internal static ReportRect GetShapeVisualBounds(ReportRect bounds, ReportShape shape)
    {
        var stroke = shape.Style.LineColor?.Alpha is > 0 ? Math.Max(0, shape.Style.LineWidth) / 2 : 0;
        var height = shape.Kind is ShapeKind.WedgeRectangleCallout or ShapeKind.WedgeRoundedRectangleCallout ? bounds.Height * 1.2 : bounds.Height;
        var expanded = new ReportRect(bounds.X - stroke, bounds.Y - stroke, bounds.Width + (stroke * 2), height + (stroke * 2));
        var rotated = GetVisualBounds(expanded, shape.Rotation);
        var offsetY = (height - bounds.Height) / 2;
        var radians = shape.Rotation * Math.PI / 180;
        return rotated with
        {
            X = rotated.X - (offsetY * Math.Sin(radians)),
            Y = rotated.Y + (offsetY * (Math.Cos(radians) - 1)),
        };
    }

    /// <summary>
    /// Gets the axis-aligned bounds of a rectangle rotated clockwise about its centre, matching the renderers.
    /// </summary>
    /// <param name="bounds">The unrotated rectangle.</param>
    /// <param name="rotation">The clockwise rotation in degrees.</param>
    /// <returns>The visual bounding rectangle.</returns>
    internal static ReportRect GetVisualBounds(ReportRect bounds, double rotation)
        => CoreIntegration.CoreModelAdapter.ToPublic(Core.Layout.ObjectGeometry.GetVisualBounds(CoreIntegration.CoreModelAdapter.ToCore(bounds), rotation));
}
