using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Shared rectangle operations in point coordinates.</summary>
internal static class RectangleGeometry
{
    /// <summary>Intersects two rectangles.</summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    /// <returns>The positive visible intersection, or null.</returns>
    internal static ReportRect? Intersect(ReportRect a, ReportRect b)
    {
        var intersection = Core.Layout.RectangleGeometry.Intersect(CoreIntegration.CoreModelAdapter.ToCore(a), CoreIntegration.CoreModelAdapter.ToCore(b));
        return intersection is { } value ? CoreIntegration.CoreModelAdapter.ToPublic(value) : null;
    }

    /// <summary>Obtains a source rectangle from one-based cell boundaries.</summary>
    /// <param name="geometry">The original worksheet geometry.</param>
    /// <param name="range">The inclusive range.</param>
    /// <returns>The source rectangle, including zero-sized hidden rows and columns.</returns>
    internal static ReportRect Bounds(SheetGeometry geometry, CellRange range) =>
        CoreIntegration.CoreModelAdapter.ToPublic(Core.Layout.RectangleGeometry.Bounds(geometry.CoreGeometry, CoreIntegration.CoreModelAdapter.ToCore(range)));
}
