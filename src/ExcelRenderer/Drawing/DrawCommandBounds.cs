using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.Drawing;

/// <summary>Conservative visible drawing bounds, excluding the renderer's initial background.</summary>
internal static class DrawCommandBounds
{
    /// <summary>Unions the clipped visible bounds of finalized commands.</summary>
    /// <param name="commands">The original commands.</param>
    /// <param name="clip">The current page or content clip.</param>
    /// <param name="fonts">The font source for compatibility text.</param>
    /// <returns>The visible union, or null when nothing draws.</returns>
    internal static ReportRect? Get(IEnumerable<DrawCommand> commands, ReportRect clip, IFontManager fonts)
    {
        ReportRect? union = null;
        foreach (var command in commands)
        {
            ReportRect? bounds = command switch
            {
                DrawViewportCommand viewport => Viewport(viewport, fonts),
                FillRectangleCommand fill when fill.Color.Alpha > 0 => fill.Bounds,
                DrawBorderCommand border => Border(border),
                DrawLineCommand line => Stroke(line.Style, line.X1, line.Y1, line.X2, line.Y2),
                DrawTextCommand text when !string.IsNullOrWhiteSpace(text.Text) => Text(text, fonts),
                DrawImageCommand image => Clip(ObjectGeometry.GetVisualBounds(image.Bounds, image.Rotation), image.ClipBounds),
                DrawShapeCommand shape => Shape(shape),
                _ => null,
            };
            union = Union(union, Clip(bounds, clip));
        }

        return union;
    }

    /// <summary>Unions two optional positive rectangles.</summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    /// <returns>The union.</returns>
    internal static ReportRect? Union(ReportRect? a, ReportRect? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        var x = Math.Min(a.Value.X, b.Value.X);
        var y = Math.Min(a.Value.Y, b.Value.Y);
        return new(
            x,
            y,
            Math.Max(a.Value.X + a.Value.Width, b.Value.X + b.Value.Width) - x,
            Math.Max(a.Value.Y + a.Value.Height, b.Value.Y + b.Value.Height) - y);
    }

    private static ReportRect? Clip(ReportRect? bounds, ReportRect? clip) =>
        bounds is null ? null : clip is null ? bounds : RectangleGeometry.Intersect(bounds.Value, clip.Value);

    private static ReportRect? Viewport(DrawViewportCommand viewport, IFontManager fonts)
    {
        var bounds = Get(viewport.Commands, viewport.Clip, fonts);
        return bounds is { } rect ? rect with { X = rect.X + viewport.OffsetX, Y = rect.Y + viewport.OffsetY } : null;
    }

    private static ReportRect? Shape(DrawShapeCommand command)
    {
        var style = command.Shape.Style;
        if (style.FillColor?.Alpha is not > 0 && style.LineColor?.Alpha is not > 0 &&
            string.IsNullOrWhiteSpace(command.Shape.Text?.Text))
        {
            return null;
        }

        return Clip(ObjectGeometry.GetShapeVisualBounds(command.Bounds, command.Shape), command.ClipBounds);
    }

    private static ReportRect? Border(DrawBorderCommand command)
    {
        var b = command.Bounds;
        return Union(
            Union(
            Stroke(command.Border.Top, b.X, b.Y, b.X + b.Width, b.Y, 0, 1),
            Stroke(command.Border.Bottom, b.X, b.Y + b.Height, b.X + b.Width, b.Y + b.Height, 0, -1)),
            Union(
                Stroke(command.Border.Left, b.X, b.Y, b.X, b.Y + b.Height, 1, 0),
                Stroke(command.Border.Right, b.X + b.Width, b.Y, b.X + b.Width, b.Y + b.Height, -1, 0)));
    }

    private static ReportRect? Stroke(BorderSide? side, double x1, double y1, double x2, double y2, double inwardX = 0, double inwardY = 0)
    {
        if (side is null || side.Color?.Alpha == 0)
        {
            return null;
        }

        ReportRect? result = null;
        var half = Math.Max(side.Width, 0.25) / 2;
        foreach (var s in BorderStrokeGeometry.GetStrokes(side, x1, y1, x2, y2, inwardX, inwardY))
        {
            result = Union(
                result,
                new ReportRect(
                Math.Min(s.X1, s.X2) - half,
                Math.Min(s.Y1, s.Y2) - half,
                Math.Abs(s.X2 - s.X1) + (2 * half),
                Math.Abs(s.Y2 - s.Y1) + (2 * half)));
        }

        return result;
    }

    private static ReportRect? Text(DrawTextCommand command, IFontManager fonts)
    {
        if (command.Style.Font.Color?.Alpha == 0)
        {
            return null;
        }

        var layout = command.TextLayout ?? new PdfSharp.PdfSharpTextMeasurer(fonts).Layout(
            command.Text,
            command.Style.Font,
            command.Bounds.Width,
            command.Style.WrapText);
        ReportRect? result = null;
        var size = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (size <= 0)
        {
            return null;
        }

        foreach (var positioned in TextLayoutPlacement.Place(layout, command.Bounds, command.Style.HorizontalAlignment, command.Style.VerticalAlignment))
        {
            foreach (var run in positioned.Line.Runs)
            {
                if (string.IsNullOrWhiteSpace(run.Run.Text))
                {
                    continue;
                }

                using Stream stream = run.Run.Font.FontData is { } bytes ? new MemoryStream(bytes, false) : File.OpenRead(run.Run.Font.FilePath);
                using var ownedFace = ConversionFontResources.Current is null ? SKTypeface.FromStream(stream) : null;
                var face = ConversionFontResources.Current?.GetTypeface(run.Run.Font) ?? ownedFace;
                using var font = new SKFont(face, (float)size);
                font.MeasureText(run.Run.Text, out var ink);
                using var glyphPath = run.Run.GlyphId is { } glyph ? font.GetGlyphPath(glyph) : null;
                if (glyphPath is not null && !glyphPath.Bounds.IsEmpty)
                {
                    ink = glyphPath.Bounds;
                }

                var x = positioned.Left + run.X;
                var baseline = positioned.Baseline;
                var bounds = new ReportRect(x + ink.Left, baseline + ink.Top, ink.Width, ink.Height);
                if (run.Run.ColorEmojiGlyphId is not null || ink.IsEmpty)
                {
                    // Color and compatibility glyphs use their finalized logical placement as a safe fallback.
                    bounds = new(
                        x - (size * .2),
                        baseline - positioned.Line.Ascent,
                        Math.Max(run.Advance, size) + (size * .4),
                        positioned.Line.Ascent + positioned.Line.Descent + positioned.Line.Leading);
                }

                result = Union(result, bounds);
            }

            if (command.Style.Font.Underline)
            {
                result = Union(result, new ReportRect(positioned.Left, positioned.Baseline + .5, positioned.Line.Width, 1));
            }
        }

        var rotation = command.Style.TextRotation == 255 ? 0 : command.Style.TextRotation;
        if (result is { } rectangle && rotation != 0)
        {
            var centerX = command.Bounds.X + (command.Bounds.Width / 2);
            var centerY = command.Bounds.Y + (command.Bounds.Height / 2);
            var angle = rotation * Math.PI / 180;
            var points = new[]
            {
                (rectangle.X, rectangle.Y),
                (rectangle.X + rectangle.Width, rectangle.Y),
                (rectangle.X, rectangle.Y + rectangle.Height),
                (rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height),
            }
                .Select(p => (X: centerX + ((p.Item1 - centerX) * Math.Cos(angle)) - ((p.Item2 - centerY) * Math.Sin(angle)),
                    Y: centerY + ((p.Item1 - centerX) * Math.Sin(angle)) + ((p.Item2 - centerY) * Math.Cos(angle)))).ToArray();
            result = new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
        }

        return command.Style.WrapText || command.Style.ShrinkToFit || rotation != 0 ? Clip(result, command.Bounds) : result;
    }
}
