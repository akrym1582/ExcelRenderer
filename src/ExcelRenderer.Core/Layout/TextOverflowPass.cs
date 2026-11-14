using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Resolves horizontal overflow after measurement and cell placement, without changing text layout.</summary>
internal sealed class TextOverflowPass : IReportLayoutPass
{
    /// <summary>Prepares limits for ordinary horizontal text wider than its original content rectangle.</summary>
    /// <param name="context">The measured sheet or page context.</param>
    public void Execute(ReportLayoutContext context)
    {
        List<CellLayout>? changed = null;
        foreach (var (address, layout) in context.CellLayouts)
        {
            var cell = context.Sheet.Cells[address];
            var style = cell.Style;
            if (string.IsNullOrEmpty(cell.Text) || cell.SuppressTextOverflow ||
                style.WrapText || style.ShrinkToFit || style.TextRotation != 0 || style.TopToBottom || layout.TextSize.Width <= layout.ContentBounds.Width)
            {
                continue;
            }

            if (context.Geometry.GetTextOverflow(context.Sheet).Find(address, cell.RowSpan, cell.ColumnSpan) is not { } limits)
            {
                continue;
            }

            var (left, right) = limits;
            var offset = layout.Bounds.X - context.Geometry.ColumnStart(address.Column);
            left += offset;
            right += offset;
            (changed ??= []).Add(layout with
            {
                TextOverflowBounds = (
                    style.HorizontalAlignment == HorizontalAlignment.Left ? layout.Bounds.X : left,
                    style.HorizontalAlignment == HorizontalAlignment.Right ? layout.Bounds.X + layout.Bounds.Width : right),
            });
        }

        foreach (var layout in changed ?? [])
        {
            context.CellLayouts[layout.Address] = layout;
        }
    }

    /// <summary>Restricts horizontal overflow to its drawing region, keeping the region's full vertical extent.</summary>
    /// <param name="layout">The original measured layout.</param>
    /// <param name="style">The effective alignment.</param>
    /// <param name="region">The source drawing region, excluding repeated-title neighbors.</param>
    /// <returns>A source-coordinate clip only when it can cut text ink.</returns>
    internal static ReportRect? GetClip(CellLayout layout, CellStyle style, ReportRect region)
    {
        if (layout.TextOverflowBounds is not { } limits)
        {
            return null;
        }

        var left = Math.Clamp(limits.Left, region.X, region.X + region.Width);
        var right = Math.Clamp(limits.Right, left, region.X + region.Width);
        var textLeft = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Right => layout.ContentBounds.X + layout.ContentBounds.Width - layout.TextSize.Width,
            HorizontalAlignment.Center => layout.ContentBounds.X + ((layout.ContentBounds.Width - layout.TextSize.Width) / 2),
            _ => layout.ContentBounds.X,
        };
        var overhang = style.Font.Size / 4;
        var clipsLeft = style.HorizontalAlignment != HorizontalAlignment.Left && textLeft - overhang < left;
        var clipsRight = style.HorizontalAlignment != HorizontalAlignment.Right && textLeft + layout.TextSize.Width + overhang > right;
        return clipsLeft || clipsRight ? new(left, region.Y, right - left, region.Height) : null;
    }

    /// <summary>Intersects optional drawing clips, preserving an empty intersection as an empty clip.</summary>
    /// <param name="first">The existing selection clip.</param>
    /// <param name="second">The text-only clip.</param>
    /// <returns>The combined clip, or null if neither is present.</returns>
    internal static ReportRect? Intersect(ReportRect? first, ReportRect? second)
    {
        if (first is not { } a)
        {
            return second;
        }

        if (second is not { } b)
        {
            return first;
        }

        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }
}
