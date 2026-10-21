using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>Maps normalized decorations from full cell bounds once, before page clipping.</summary>
internal static class ConditionalCellVisualGeometry
{
    /// <summary>Computes the icon width before page scaling.</summary>
    /// <returns>The generated geometry.</returns>
    /// <param name="ordinaryContentWidth">Ordinary source text width in points.</param>
    internal static double IconSize(double ordinaryContentWidth) => Math.Min(12, Math.Max(0, ordinaryContentWidth - 2));

    /// <summary>Checks the same width reservation used during text measurement.</summary>
    /// <param name="cell">Placed cell and immutable visual payload.</param>
    /// <returns>Whether the icon has usable space.</returns>
    internal static bool CanDrawIcon(Core.Layout.RenderCell cell) => SourceIconSize(cell) >= 3 && cell.Bounds.Height > (2 * cell.DecorationScale);

    /// <summary>Builds decorations from full cell geometry before applying all viewports.</summary>
    /// <param name="page">Output page number.</param>
    /// <param name="cell">Placed cell and immutable visual payload.</param>
    /// <returns>The generated geometry.</returns>
    internal static IEnumerable<DrawCommand> Create(int page, Core.Layout.RenderCell cell)
    {
        var visual = FullCellPresentationData.Decoration(cell.Cell.ExtensionData);
        if (visual is null)
        {
            yield break;
        }

        var bounds = CoreModelAdapter.ToPublic(cell.Bounds);
        var scale = cell.DecorationScale;
        var clip = bounds;
        if (cell.ClipBounds is { } selection)
        {
            clip = RectangleGeometry.Intersect(clip, CoreModelAdapter.ToPublic(selection)) ?? default;
        }

        if (cell.DecorationClipBounds is { } region)
        {
            clip = RectangleGeometry.Intersect(clip, CoreModelAdapter.ToPublic(region)) ?? default;
        }

        if (clip.Width <= 0 || clip.Height <= 0)
        {
            yield break;
        }

        var commands = new List<DrawCommand>();
        var inner = new ReportRect(bounds.X + scale, bounds.Y + scale, Math.Max(0, bounds.Width - (2 * scale)), Math.Max(0, bounds.Height - (2 * scale)));
        if (visual.DataBar is { } bar && inner.Width > 0 && inner.Height > 0)
        {
            var rectangle = new ReportRect(inner.X + (bar.Start * inner.Width), inner.Y, (bar.End - bar.Start) * inner.Width, inner.Height);
            if (rectangle.Width > 0)
            {
                commands.Add(new FillRectangleCommand(page, rectangle, CoreModelAdapter.ToPublic(bar.Fill)));
                if (bar.Border is { } border)
                {
                    var line = Math.Min(.5 * scale, Math.Min(rectangle.Width, rectangle.Height));
                    Fill(new(rectangle.X, rectangle.Y, rectangle.Width, line), border);
                    Fill(new(rectangle.X, rectangle.Y + rectangle.Height - line, rectangle.Width, line), border);
                    Fill(new(rectangle.X, rectangle.Y, line, rectangle.Height), border);
                    Fill(new(rectangle.X + rectangle.Width - line, rectangle.Y, line, rectangle.Height), border);
                }
            }

            if (bar.Axis is { } axis && bar.AxisColor is { } axisColor)
            {
                Fill(new(inner.X + (axis * inner.Width) - (.25 * scale), inner.Y, .5 * scale, inner.Height), axisColor);
            }
        }

        if (visual.Icon is { } icon && CanDrawIcon(cell))
        {
            var size = SourceIconSize(cell) * scale;
            var height = Math.Min(size, inner.Height);
            var iconBounds = new ReportRect(icon.RightToLeft ? bounds.X + bounds.Width - scale - size : bounds.X + scale, bounds.Y + ((bounds.Height - height) / 2), size, height);
            commands.AddRange(ConditionalIconGeometry.Create(page, icon.Set, icon.Index, iconBounds));
        }

        if (commands.Count > 0)
        {
            yield return new DrawViewportCommand(page, commands, clip, 0, 0);
        }

        void Fill(ReportRect rect, Core.Model.ReportColor color) => commands.Add(new FillRectangleCommand(page, rect, CoreModelAdapter.ToPublic(color)));
    }

    private static double SourceIconSize(Core.Layout.RenderCell cell)
    {
        var source = new Core.Layout.ReportRect(0, 0, cell.Bounds.Width / cell.DecorationScale, cell.Bounds.Height / cell.DecorationScale);
        var style = cell.Cell.Style with { Font = cell.Cell.Style.Font with { Size = cell.Cell.Style.Font.Size / cell.DecorationScale } };
        return IconSize(Core.Layout.CellContentBounds.Calculate(source, style).Width);
    }
}
