using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>Fixed normalized Phase 3 vector assets, independent of Excel images and fonts.</summary>
internal static class ConditionalIconGeometry
{
    private static readonly IReadOnlyList<(double X, double Y)> Arrow = Array.AsReadOnly(new (double, double)[] { (.5, .05), (.95, .5), (.68, .5), (.68, .95), (.32, .95), (.32, .5), (.05, .5) });
    private static readonly IReadOnlyList<(double X, double Y)> Check = Array.AsReadOnly(new (double, double)[] { (.12, .50), (.27, .37), (.43, .55), (.76, .16), (.91, .29), (.44, .85) });
    private static readonly IReadOnlyList<(double X, double Y)> Cross1 = Array.AsReadOnly(new (double, double)[] { (.15, .25), (.25, .15), (.85, .75), (.75, .85) });
    private static readonly IReadOnlyList<(double X, double Y)> Cross2 = Array.AsReadOnly(new (double, double)[] { (.15, .75), (.75, .15), (.85, .25), (.25, .85) });
    private static readonly IReadOnlyList<ReportColor> Colors = Array.AsReadOnly(new ReportColor[] { new(192, 80, 77), new(229, 183, 59), new(99, 163, 92) });
    private static readonly IReadOnlyList<ReportColor> Grays = Array.AsReadOnly(new ReportColor[] { new(89, 89, 89), new(140, 140, 140), new(191, 191, 191) });

    /// <summary>Maps a selected fixed icon asset into a page-space rectangle.</summary>
    /// <param name="page">The page input.</param>
    /// <param name="set">The set input.</param>
    /// <param name="index">The index input.</param>
    /// <param name="bounds">The bounds input.</param>
    /// <returns>Font-independent vector commands.</returns>
    internal static IEnumerable<DrawCommand> Create(int page, string set, int index, ReportRect bounds)
    {
        var color = (set == "3ArrowsGray" ? Grays : Colors)[index];
        if (set is "3Arrows" or "3ArrowsGray")
        {
            yield return Polygon(Arrow.Select(point => index == 2 ? point : index == 1 ? (1 - point.Y, point.X) : (1 - point.X, 1 - point.Y)), color, bounds);
            yield break;
        }

        if (set is "3TrafficLights1" or "3Symbols")
        {
            var circle = new ReportRect(bounds.X + (.08 * bounds.Width), bounds.Y + (.08 * bounds.Height), .84 * bounds.Width, .84 * bounds.Height);
            yield return new DrawShapeCommand(page, circle, new(new(1, 1), 0, 0, circle.Width, circle.Height, ShapeKind.Ellipse, new(color, null), null, 0, 0));
            if (set == "3TrafficLights1")
            {
                yield break;
            }

            color = new(255, 255, 255);
            bounds = new(bounds.X + (.14 * bounds.Width), bounds.Y + (.14 * bounds.Height), .72 * bounds.Width, .72 * bounds.Height);
        }

        if (index == 0)
        {
            yield return Polygon(Cross1, color, bounds);
            yield return Polygon(Cross2, color, bounds);
        }
        else if (index == 1)
        {
            yield return new FillRectangleCommand(page, new(bounds.X + (.43 * bounds.Width), bounds.Y + (.15 * bounds.Height), .14 * bounds.Width, .45 * bounds.Height), color);
            yield return new FillRectangleCommand(page, new(bounds.X + (.43 * bounds.Width), bounds.Y + (.72 * bounds.Height), .14 * bounds.Width, .14 * bounds.Height), color);
        }
        else
        {
            yield return Polygon(Check, color, bounds);
        }

        DrawPolygonCommand Polygon(IEnumerable<(double X, double Y)> points, ReportColor fill, ReportRect rect) => new(page, Array.AsReadOnly(points.Select(point => (rect.X + (point.X * rect.Width), rect.Y + (point.Y * rect.Height))).ToArray()), fill, rect);
    }
}
