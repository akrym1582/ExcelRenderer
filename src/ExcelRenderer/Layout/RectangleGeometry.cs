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
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        var width = Math.Min(a.X + a.Width, b.X + b.Width) - x;
        var height = Math.Min(a.Y + a.Height, b.Y + b.Height) - y;
        return width > 0 && height > 0 ? new(x, y, width, height) : null;
    }

    /// <summary>Obtains a source rectangle from one-based cell boundaries.</summary>
    /// <param name="geometry">The original worksheet geometry.</param>
    /// <param name="range">The inclusive range.</param>
    /// <returns>The source rectangle, including zero-sized hidden rows and columns.</returns>
    internal static ReportRect Bounds(SheetGeometry geometry, CellRange range) => new(
        geometry.ColumnStart(range.First.Column),
        geometry.RowStart(range.First.Row),
        geometry.ColumnStart(range.Last.Column + 1) - geometry.ColumnStart(range.First.Column),
        geometry.RowStart(range.Last.Row + 1) - geometry.RowStart(range.First.Row));
}
