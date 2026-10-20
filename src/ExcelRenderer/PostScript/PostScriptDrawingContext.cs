using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;
using static ExcelRenderer.PostScript.PostScriptWriter;

namespace ExcelRenderer.PostScript;

/// <summary>Consumes finalized drawing commands in points, with no page rasterization.</summary>
internal sealed class PostScriptDrawingContext
{
    private readonly PostScriptFontCollection fonts;
    private readonly PostScriptWriter? writer;
    private readonly CancellationToken token;
    private readonly int page;
    private int textId;

    /// <summary>Initializes a new instance of the <see cref="PostScriptDrawingContext"/> class.</summary>
    /// <param name="fonts">Document-shared fonts.</param>
    /// <param name="writer">Null for precollection and validation.</param>
    /// <param name="token">The cancellation token.</param>
    /// <param name="page">The output page number.</param>
    internal PostScriptDrawingContext(PostScriptFontCollection fonts, PostScriptWriter? writer, CancellationToken token, int page = 0)
    {
        this.fonts = fonts;
        this.writer = writer;
        this.token = token;
        this.page = page;
    }

    /// <summary>Collects or paints a command while preserving nested graphics state.</summary>
    /// <param name="command">The finalized command.</param>
    internal void Execute(DrawCommand command)
    {
        token.ThrowIfCancellationRequested();
        switch (command)
        {
            case DrawViewportCommand viewport:
                writer?.Line($"gsave {Number(viewport.OffsetX)} {Number(viewport.OffsetY)} translate");
                writer?.Clip(viewport.Clip);
                foreach (var child in viewport.Commands)
                {
                    Execute(child);
                }

                writer?.Line("grestore");
                break;
            case FillRectangleCommand fill:
                if (Visible(fill.Color))
                {
                    writer?.Color(fill.Color);
                    writer?.Rectangle(fill.Bounds);
                    writer?.Line("fill");
                }

                break;
            case DrawLineCommand line:
                Line(line.Style, line.X1, line.Y1, line.X2, line.Y2);
                break;
            case DrawBorderCommand border:
                var b = border.Bounds;
                Line(border.Border.Top, b.X, b.Y, b.X + b.Width, b.Y, 0, 1);
                Line(border.Border.Right, b.X + b.Width, b.Y, b.X + b.Width, b.Y + b.Height, -1, 0);
                Line(border.Border.Bottom, b.X, b.Y + b.Height, b.X + b.Width, b.Y + b.Height, 0, -1);
                Line(border.Border.Left, b.X, b.Y, b.X, b.Y + b.Height, 1, 0);
                break;
            case DrawTextCommand text:
                Text(text);
                break;
            case DrawImageCommand image:
                Image(image);
                break;
            case DrawShapeCommand shape:
                Shape(shape);
                break;
            default:
                throw new PostScriptFailure("PostScriptCommandUnsupported", $"Unsupported drawing command: {command.GetType().Name}.");
        }
    }

    private void Text(DrawTextCommand command)
    {
        var layout = command.TextLayout ?? throw new InvalidOperationException("Text must be finalized before PostScript drawing.");
        var id = ++textId;
        writer?.Metadata(binary =>
        {
            binary.Write((byte)0);
            binary.Write(page);
            binary.Write(id);
            binary.Write(command.OriginalText ?? command.Text);
        });
        var size = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (size == 0)
        {
            return;
        }

        writer?.Line("gsave");
        if (command.Style.WrapText || command.Style.ShrinkToFit || command.Style.TextRotation is not (0 or 255))
        {
            writer?.Clip(command.Bounds);
        }

        if (command.Style.TextRotation is not (0 or 255))
        {
            writer?.Rotate(command.Style.TextRotation, command.Bounds.X + (command.Bounds.Width / 2), command.Bounds.Y + (command.Bounds.Height / 2));
        }

        foreach (var positioned in TextLayoutPlacement.Place(layout, command.Bounds, command.Style.HorizontalAlignment, command.Style.VerticalAlignment))
        {
            foreach (var run in positioned.Line.Runs)
            {
                var style = run.Font ?? command.Style.Font;
                if (!Visible(style.Color ?? new(0, 0, 0)))
                {
                    continue;
                }

                if (writer is null)
                {
                    fonts.Collect(run);
                    continue;
                }

                var fontSize = run.Font?.Size ?? size;
                var x = positioned.Left + run.X;
                var encoded = fonts.Encode(run).ToArray();
                var selection = run.Glyphs!;
                writer.Metadata(binary =>
                {
                    binary.Write((byte)1);
                    binary.Write(page);
                    binary.Write(id);
                    binary.Write(run.Run.SourceText);
                    binary.Write(selection.FontIdentity);
                    binary.Write(x);
                    binary.Write(positioned.Baseline);
                    binary.Write(fontSize);
                    binary.Write(encoded.Length);
                    for (var i = 0; i < encoded.Length; i++)
                    {
                        binary.Write(encoded[i].Font);
                        binary.Write(encoded[i].Code);
                        binary.Write(encoded[i].Glyph);
                        binary.Write(selection.Clusters[i]);
                        binary.Write(selection.Advances[i]);
                    }
                });
                writer.Color(style.Color ?? new(0, 0, 0));
                for (var i = 0; i < encoded.Length; i++)
                {
                    var glyph = encoded[i];

                    // Page coordinates point down. A negative Y font scale restores upright glyphs.
                    writer.Line($"/{glyph.Font} findfont [{Number(fontSize)} 0 0 {Number(-fontSize)} 0 0] makefont setfont");
                    writer.Line($"{Number(x)} {Number(positioned.Baseline)} moveto <{glyph.Code:X2}> show");
                    x += selection.Advances[i] * fontSize;
                }

                if (run.Font?.Underline == true)
                {
                    Line(new(1, style.Color), positioned.Left + run.X, positioned.Baseline + 1, positioned.Left + run.X + run.Advance, positioned.Baseline + 1);
                }
            }

            if (command.Style.Font.Underline && !positioned.Line.Runs.Any(run => run.Font is not null))
            {
                Line(new(1, command.Style.Font.Color), positioned.Left, positioned.Baseline + 1, positioned.Left + positioned.Line.Width, positioned.Baseline + 1);
            }
        }

        writer?.Line("grestore");
    }

    private void Line(BorderSide? side, double x1, double y1, double x2, double y2, double inwardX = 0, double inwardY = 0)
    {
        if (side is null || !Visible(side.Color ?? new(0, 0, 0)))
        {
            return;
        }

        if (side.Width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(side), "Stroke width must be nonnegative.");
        }

        writer?.Color(side.Color ?? new(0, 0, 0));
        var pattern = side.LineStyle switch
        {
            BorderLineStyle.Dotted => new[] { 1, 2 },
            BorderLineStyle.Dashed => new[] { 3, 2 },
            BorderLineStyle.DashDot => new[] { 3, 2, 1, 2 },
            BorderLineStyle.DashDotDot => new[] { 3, 2, 1, 2, 1, 2 },
            _ => Array.Empty<int>(),
        };
        writer?.Line($"{Number(side.Width)} setlinewidth [{string.Join(" ", pattern.Select(value => Number(value * side.Width)))}] 0 setdash");
        foreach (var stroke in BorderStrokeGeometry.GetStrokes(side, x1, y1, x2, y2, inwardX, inwardY))
        {
            writer?.Line($"newpath {Number(stroke.X1)} {Number(stroke.Y1)} moveto {Number(stroke.X2)} {Number(stroke.Y2)} lineto stroke");
        }
    }

    private void Image(DrawImageCommand command)
    {
        using var ownedResources = ImageResources.Current is null ? new ImageResources() : null;
        using var lease = ImageResources.Current!.AcquireImage(command.ImageBytes, out var failure);
        if (lease is null)
        {
            throw new PostScriptFailure("PostScriptImageDecodeFailed", failure ?? "Image decoding failed.");
        }

        using var bitmap = SKBitmap.FromImage(lease.Value.Raster);
        var visible = false;
        var transparent = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < bitmap.Width; x++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                if (alpha is > 0 and < 255)
                {
                    throw new PostScriptFailure("PostScriptTransparencyUnsupported", "PostScript output does not support image pixels with partial alpha.");
                }

                visible |= alpha != 0;
                transparent |= alpha == 0;
            }
        }

        if (!visible)
        {
            return;
        }

        if (transparent)
        {
            throw new PostScriptFailure("PostScriptImageTransparencyUnsupported", "PostScript output does not support binary image transparency masks.");
        }

        if (writer is null)
        {
            return;
        }

        var b = command.Bounds;
        var crop = command.Crop ?? new ImageCrop(0, 0, 0, 0);
        var width = 1 - crop.Left - crop.Right;
        var height = 1 - crop.Top - crop.Bottom;
        if (width <= 0 || height <= 0 || b.Width <= 0 || b.Height <= 0)
        {
            return;
        }

        writer.Line("gsave");
        if (command.ClipBounds is { } clip)
        {
            writer.Clip(clip);
        }

        var cx = b.X + (b.Width / 2);
        var cy = b.Y + (b.Height / 2);
        writer.Rotate(command.Rotation, cx, cy);
        writer.Line($"{Number(cx)} {Number(cy)} translate {(command.FlipHorizontal ? -1 : 1)} {(command.FlipVertical ? -1 : 1)} scale {Number(-cx)} {Number(-cy)} translate");
        writer.Clip(b);
        writer.Line($"{Number(b.X - (crop.Left * b.Width / width))} {Number(b.Y - (crop.Top * b.Height / height))} translate {Number(b.Width / width)} {Number(b.Height / height)} scale");
        writer.Line("/DeviceRGB setcolorspace");
        writer.Line($"<< /ImageType 1 /Width {bitmap.Width} /Height {bitmap.Height} /BitsPerComponent 8 /Decode [0 1 0 1 0 1] /ImageMatrix [{bitmap.Width} 0 0 {bitmap.Height} 0 0] /DataSource currentfile /ASCIIHexDecode filter /Interpolate true >> image");
        var row = new byte[checked(bitmap.Width * 3)];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                row[x * 3] = color.Red;
                row[(x * 3) + 1] = color.Green;
                row[(x * 3) + 2] = color.Blue;
            }

            writer.Hex(row, 0, row.Length);
        }

        writer.Line(">");
        writer.Line("grestore");
    }

    private void Shape(DrawShapeCommand command)
    {
        var style = command.Shape.Style;
        var fill = style.FillColor is { } f && Visible(f);
        var stroke = style.LineColor is { } s && Visible(s);
        if (stroke && style.LineWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Shape stroke width must be nonnegative.");
        }

        writer?.Line("gsave");
        if (command.ClipBounds is { } clip)
        {
            writer?.Clip(clip);
        }

        var b = command.Bounds;
        writer?.Rotate(command.Shape.Rotation, b.X + (b.Width / 2), b.Y + (b.Height / 2));
        if (writer is not null && (fill || stroke))
        {
            ShapePath(b, command.Shape.Kind);
            if (fill)
            {
                writer.Color(style.FillColor!.Value);
                writer.Line("gsave fill grestore");
            }

            if (stroke)
            {
                writer.Color(style.LineColor!.Value);
                writer.Line($"{Number(style.LineWidth)} setlinewidth [] 0 setdash stroke");
            }
            else
            {
                writer.Line("newpath");
            }
        }

        if (command.TextCommand is { } text)
        {
            Text(text);
        }

        writer?.Line("grestore");
    }

    private void ShapePath(ReportRect b, ShapeKind kind)
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
            var rx = (float)Math.Min(10, b.Width / 4) / 2;
            var ry = (float)Math.Min(10, b.Height / 4) / 2;
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

        using var path = builder.Detach();
        using var iterator = path.CreateIterator(false);
        var points = new SKPoint[4];
        writer!.Line("newpath");
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    writer.Line($"{Number(points[0].X)} {Number(points[0].Y)} moveto");
                    break;
                case SKPathVerb.Line:
                    writer.Line($"{Number(points[1].X)} {Number(points[1].Y)} lineto");
                    break;
                case SKPathVerb.Cubic:
                    writer.Line($"{Number(points[1].X)} {Number(points[1].Y)} {Number(points[2].X)} {Number(points[2].Y)} {Number(points[3].X)} {Number(points[3].Y)} curveto");
                    break;
                case SKPathVerb.Quad:
                    var c1 = new SKPoint(points[0].X + ((points[1].X - points[0].X) * (2f / 3)), points[0].Y + ((points[1].Y - points[0].Y) * (2f / 3)));
                    var c2 = new SKPoint(points[2].X + ((points[1].X - points[2].X) * (2f / 3)), points[2].Y + ((points[1].Y - points[2].Y) * (2f / 3)));
                    writer.Line($"{Number(c1.X)} {Number(c1.Y)} {Number(c2.X)} {Number(c2.Y)} {Number(points[2].X)} {Number(points[2].Y)} curveto");
                    break;
                case SKPathVerb.Conic:
                    throw new InvalidOperationException("Conic shape paths must be converted to cubic geometry.");
                case SKPathVerb.Close:
                    writer.Line("closepath");
                    break;
            }
        }
    }
}
