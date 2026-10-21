using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>A closed, filled vector polygon in page points, without font dependencies.</summary>
internal sealed record DrawPolygonCommand(int PageNumber, IReadOnlyList<(double X, double Y)> Vertices, ReportColor Color, ReportRect Bounds) : DrawCommand(PageNumber)
{
    /// <summary>Gets ClipBounds for the saved visual profile.</summary>
    internal ReportRect? ClipBounds { get; init; }
}
