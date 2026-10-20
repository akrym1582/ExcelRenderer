using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.Drawing;

/// <summary>Shares shape dimensions and outlines while preserving each backend's native curve representation.</summary>
internal static class ShapeGeometry
{
    /// <summary>Paints the established native Skia primitives, retaining their exact raster and PDF behavior.</summary>
    /// <param name="canvas">The current transformed canvas.</param>
    /// <param name="bounds">The unrotated point rectangle.</param>
    /// <param name="kind">The saved shape kind.</param>
    /// <param name="paint">The caller-owned fill or stroke.</param>
    internal static void DrawSkia(SKCanvas canvas, ReportRect bounds, ShapeKind kind, SKPaint paint)
    {
        var rect = new SKRect((float)bounds.X, (float)bounds.Y, (float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height));
        if (kind == ShapeKind.Ellipse)
        {
            canvas.DrawOval(rect, paint);
        }
        else if (kind == ShapeKind.RoundedRectangle)
        {
            var radius = Radii(bounds);
            canvas.DrawRoundRect(rect, radius.X, radius.Y, paint);
        }
        else if (kind is ShapeKind.WedgeRectangleCallout or ShapeKind.WedgeRoundedRectangleCallout)
        {
            using var path = CreateCubicPath(bounds, kind);
            canvas.DrawPath(path, paint);
        }
        else
        {
            canvas.DrawRect(rect, paint);
        }
    }

    /// <summary>Creates the shared polygon or established PostScript cubic outline. The caller disposes it.</summary>
    /// <param name="b">The unrotated point rectangle.</param>
    /// <param name="kind">The saved shape kind.</param>
    /// <returns>An owned path without conic verbs.</returns>
    internal static SKPath CreateCubicPath(ReportRect b, ShapeKind kind)
    {
        using var builder = new SKPathBuilder();
        var rect = new SKRect((float)b.X, (float)b.Y, (float)(b.X + b.Width), (float)(b.Y + b.Height));
        if (kind == ShapeKind.Ellipse)
        {
            var cx = rect.MidX;
            var cy = rect.MidY;
            var rx = rect.Width / 2;
            var ry = rect.Height / 2;
            const float k = .552284749831f;
            builder.MoveTo(cx + rx, cy);
            builder.CubicTo(cx + rx, cy + (k * ry), cx + (k * rx), cy + ry, cx, cy + ry);
            builder.CubicTo(cx - (k * rx), cy + ry, cx - rx, cy + (k * ry), cx - rx, cy);
            builder.CubicTo(cx - rx, cy - (k * ry), cx - (k * rx), cy - ry, cx, cy - ry);
            builder.CubicTo(cx + (k * rx), cy - ry, cx + rx, cy - (k * ry), cx + rx, cy);
            builder.Close();
        }
        else if (kind == ShapeKind.RoundedRectangle)
        {
            var radius = Radii(b);
            var rx = radius.X;
            var ry = radius.Y;
            const float k = .552284749831f;
            builder.MoveTo(rect.Left + rx, rect.Top);
            builder.LineTo(rect.Right - rx, rect.Top);
            builder.CubicTo(rect.Right - rx + (k * rx), rect.Top, rect.Right, rect.Top + ry - (k * ry), rect.Right, rect.Top + ry);
            builder.LineTo(rect.Right, rect.Bottom - ry);
            builder.CubicTo(rect.Right, rect.Bottom - ry + (k * ry), rect.Right - rx + (k * rx), rect.Bottom, rect.Right - rx, rect.Bottom);
            builder.LineTo(rect.Left + rx, rect.Bottom);
            builder.CubicTo(rect.Left + rx - (k * rx), rect.Bottom, rect.Left, rect.Bottom - ry + (k * ry), rect.Left, rect.Bottom - ry);
            builder.LineTo(rect.Left, rect.Top + ry);
            builder.CubicTo(rect.Left, rect.Top + ry - (k * ry), rect.Left + rx - (k * rx), rect.Top, rect.Left + rx, rect.Top);
            builder.Close();
        }
        else if (kind is ShapeKind.WedgeRectangleCallout or ShapeKind.WedgeRoundedRectangleCallout)
        {
            builder.MoveTo(rect.Left, rect.Top);
            builder.LineTo(rect.Right, rect.Top);
            builder.LineTo(rect.Right, rect.Bottom);
            builder.LineTo(rect.Left + (rect.Width * .35f), rect.Bottom);
            builder.LineTo(rect.Left + (rect.Width * .15f), rect.Bottom + (rect.Height * .2f));
            builder.LineTo(rect.Left + (rect.Width * .2f), rect.Bottom);
            builder.LineTo(rect.Left, rect.Bottom);
            builder.Close();
        }
        else
        {
            builder.AddRect(rect);
        }

        return builder.Detach();
    }

    private static SKPoint Radii(ReportRect bounds) => new((float)Math.Min(10, bounds.Width / 4) / 2, (float)Math.Min(10, bounds.Height / 4) / 2);
}
