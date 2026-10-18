using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Model;
using SkiaSharp;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Shares cell backgrounds and styled border strokes across every Skia output.</summary>
internal static class SkiaDrawingPrimitives
{
    /// <summary>Paints PDF images while retaining full source pixels at cropped interpolation edges.</summary>
    /// <param name="canvas">The target PDF canvas.</param>
    /// <param name="image">The validated source image.</param>
    /// <param name="source">The source crop in image pixels.</param>
    /// <param name="destination">The destination in points.</param>
    internal static void DrawPdfImage(SKCanvas canvas, SKImage image, SKRect source, SKRect destination)
    {
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        if (source == new SKRect(0, 0, image.Width, image.Height))
        {
            canvas.DrawImage(image, source, destination, sampling);
            return;
        }

        if (source.Width <= 0 || source.Height <= 0)
        {
            return;
        }

        // A raster image permits Skia's PDF writer to trim to integer pixel bounds,
        // changing edge interpolation compared with its original encoded-image path.
        // Clip the mapped full image instead, without a second decode or new bitmap.
        canvas.Save();
        canvas.ClipRect(destination);
        var scaleX = destination.Width / source.Width;
        var scaleY = destination.Height / source.Height;
        canvas.Concat(new SKMatrix
        {
            ScaleX = scaleX,
            ScaleY = scaleY,
            TransX = destination.Left - (source.Left * scaleX),
            TransY = destination.Top - (source.Top * scaleY),
            Persp2 = 1,
        });
        canvas.DrawImage(image, 0, 0, sampling);
        canvas.Restore();
    }

    /// <summary>Paints all styled cell edges through shared stroke geometry.</summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="command">The border command.</param>
    internal static void DrawBorder(SKCanvas canvas, DrawBorderCommand command)
    {
        var rect = command.Bounds;
        DrawSide(command.Border.Top, rect.X, rect.Y, rect.X + rect.Width, rect.Y, 0, 1);
        DrawSide(command.Border.Right, rect.X + rect.Width, rect.Y, rect.X + rect.Width, rect.Y + rect.Height, -1, 0);
        DrawSide(command.Border.Bottom, rect.X, rect.Y + rect.Height, rect.X + rect.Width, rect.Y + rect.Height, 0, -1);
        DrawSide(command.Border.Left, rect.X, rect.Y, rect.X, rect.Y + rect.Height, 1, 0);

        void DrawSide(BorderSide? side, double x1, double y1, double x2, double y2, double inwardX, double inwardY)
        {
            if (side is null)
            {
                return;
            }

            DrawStyledLine(canvas, side, x1, y1, x2, y2, inwardX, inwardY);
        }
    }

    /// <summary>Paints one styled line, preserving inward offsets and double strokes.</summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="side">The line style.</param>
    /// <param name="x1">The start X.</param>
    /// <param name="y1">The start Y.</param>
    /// <param name="x2">The end X.</param>
    /// <param name="y2">The end Y.</param>
    /// <param name="inwardX">The inward X direction.</param>
    /// <param name="inwardY">The inward Y direction.</param>
    internal static void DrawStyledLine(SKCanvas canvas, BorderSide side, double x1, double y1, double x2, double y2, double inwardX = 0, double inwardY = 0)
    {
        using var paint = CreateBorderPaint(side);
        foreach (var stroke in BorderStrokeGeometry.GetStrokes(side, x1, y1, x2, y2, inwardX, inwardY))
        {
            canvas.DrawLine((float)stroke.X1, (float)stroke.Y1, (float)stroke.X2, (float)stroke.Y2, paint);
        }
    }

    /// <summary>Creates an antialiased paint in point units.</summary>
    /// <param name="color">The RGBA color.</param>
    /// <param name="style">The paint style.</param>
    /// <param name="width">The stroke width.</param>
    /// <returns>The caller-owned paint.</returns>
    internal static SKPaint CreatePaint(ReportColor color, SKPaintStyle style, double width = 1) => new()
    {
        Color = new(color.Red, color.Green, color.Blue, color.Alpha),
        Style = style,
        StrokeWidth = (float)width,
        IsAntialias = true,
    };

    private static SKPaint CreateBorderPaint(BorderSide side)
    {
        var paint = CreatePaint(side.Color ?? new(0, 0, 0), SKPaintStyle.Stroke, side.Width);
        var pattern = side.LineStyle switch
        {
            BorderLineStyle.Dotted => new float[] { 1, 2 },
            BorderLineStyle.Dashed => new float[] { 3, 2 },
            BorderLineStyle.DashDot => new float[] { 3, 2, 1, 2 },
            BorderLineStyle.DashDotDot => new float[] { 3, 2, 1, 2, 1, 2 },
            _ => null,
        };
        if (pattern is not null)
        {
            using var effect = SKPathEffect.CreateDash(pattern.Select(x => x * (float)side.Width).ToArray(), 0);
            paint.PathEffect = effect;
        }

        return paint;
    }
}
