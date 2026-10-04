using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>PNG と SVG で共有する SkiaSharp 描画コマンド実行処理です。</summary>
internal sealed class SkiaDrawingContext
{
    private readonly SkiaTextPainter _textPainter;

    /// <summary>Initializes a new instance of the <see cref="SkiaDrawingContext"/> class. PNG または SVG の描画先へコマンドを実行するコンテキストを初期化します。</summary>
    /// <param name="textAsPaths">true の場合は文字をパスとして描画します。</param>
    /// <param name="fontManager">文字の描画に使用するフォントを解決するマネージャーです。指定しない場合は Skia のシステムフォント選択を使用します。</param>
    internal SkiaDrawingContext(bool textAsPaths, IFontManager? fontManager = null)
    {
        _textPainter = new(textAsPaths, fontManager);
    }

    /// <summary>単一の描画コマンドを実行します。</summary>
    /// <param name="canvas">描画先のキャンバスです。</param>
    /// <param name="command">実行する描画コマンドです。</param>
    internal void Execute(SKCanvas canvas, DrawCommand command)
    {
        switch (command)
        {
            case FillRectangleCommand fill:
                using (var paint = CreatePaint(fill.Color, SKPaintStyle.Fill))
                {
                    canvas.DrawRect(ToRect(fill.Bounds), paint);
                }

                break;
            case DrawBorderCommand border:
                DrawBorder(canvas, border);
                break;
            case DrawTextCommand text:
                _textPainter.Paint(canvas, text);
                break;
            case DrawLineCommand line:
                DrawStyledLine(canvas, line.Style, line.X1, line.Y1, line.X2, line.Y2);

                break;
            case DrawImageCommand image:
                DrawImage(canvas, image);
                break;
            case DrawShapeCommand shape:
                DrawShape(canvas, shape);
                break;
        }
    }

    private static SKRect ToRect(ReportRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

    private void DrawShape(SKCanvas canvas, DrawShapeCommand command)
    {
        var b = ToRect(command.Bounds);
        canvas.Save();
        if (command.ClipBounds is { } clip)
        {
            canvas.ClipRect(ToRect(clip));
        }

        if (command.Shape.Rotation != 0)
        {
            canvas.RotateDegrees((float)command.Shape.Rotation, b.MidX, b.MidY);
        }

        void Paint(SKPaintStyle style, ReportColor color, Action<SKPaint> draw)
        {
            using var paint = CreatePaint(color, style, command.Shape.Style.LineWidth);
            draw(paint);
        }

        void Draw(SKPaint paint)
        {
            if (command.Shape.Kind == ShapeKind.Ellipse)
            {
                canvas.DrawOval(b, paint);
            }
            else if (command.Shape.Kind == ShapeKind.RoundedRectangle)
            {
                canvas.DrawRoundRect(b, 10, 10, paint);
            }
            else if (command.Shape.Kind is ShapeKind.WedgeRectangleCallout or ShapeKind.WedgeRoundedRectangleCallout)
            {
                using var pathBuilder = new SKPathBuilder();
                pathBuilder.MoveTo(b.Left, b.Top);
                pathBuilder.LineTo(b.Right, b.Top);
                pathBuilder.LineTo(b.Right, b.Bottom);
                pathBuilder.LineTo(b.Left + (b.Width * .35f), b.Bottom);
                pathBuilder.LineTo(b.Left + (b.Width * .15f), b.Bottom + (b.Height * .2f));
                pathBuilder.LineTo(b.Left + (b.Width * .2f), b.Bottom);
                pathBuilder.LineTo(b.Left, b.Bottom);
                pathBuilder.Close();
                using var path = pathBuilder.Detach();
                canvas.DrawPath(path, paint);
            }
            else
            {
                canvas.DrawRect(b, paint);
            }
        }

        if (command.Shape.Style.FillColor is { } fill)
        {
            Paint(SKPaintStyle.Fill, fill, Draw);
        }

        if (command.Shape.Style.LineColor is { } line)
        {
            Paint(SKPaintStyle.Stroke, line, Draw);
        }

        if (command.Shape.Text is { } text)
        {
            var bounds = new ReportRect(
                command.Bounds.X + text.MarginLeft,
                command.Bounds.Y + text.MarginTop,
                Math.Max(0, command.Bounds.Width - text.MarginLeft - text.MarginRight),
                Math.Max(0, command.Bounds.Height - text.MarginTop - text.MarginBottom));
            _textPainter.Paint(canvas, new DrawTextCommand(command.PageNumber, bounds, text.Text, CellStyle.Default with
            { Font = text.Font, HorizontalAlignment = text.HorizontalAlignment, VerticalAlignment = text.VerticalAlignment, WrapText = text.WrapText }));
        }

        canvas.Restore();
    }

    private void DrawImage(SKCanvas canvas, DrawImageCommand command)
    {
        using var image = SKImage.FromEncodedData(command.ImageBytes)
            ?? throw new InvalidDataException("画像データを読み込めません。");
        var destination = ToRect(command.Bounds);
        var source = command.Crop is not { } crop
            ? new SKRect(0, 0, image.Width, image.Height)
            : new SKRect(
                (float)(crop.Left * image.Width),
                (float)(crop.Top * image.Height),
                (float)((1 - crop.Right) * image.Width),
                (float)((1 - crop.Bottom) * image.Height));
        canvas.Save();
        if (command.ClipBounds is { } clip)
        {
            canvas.ClipRect(ToRect(clip));
        }

        canvas.RotateDegrees((float)command.Rotation, destination.MidX, destination.MidY);
        canvas.Scale(
            command.FlipHorizontal ? -1 : 1,
            command.FlipVertical ? -1 : 1,
            destination.MidX,
            destination.MidY);
        canvas.DrawImage(image, source, destination, new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Restore();
    }

    private void DrawBorder(SKCanvas canvas, DrawBorderCommand command)
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

    private void DrawStyledLine(SKCanvas canvas, BorderSide side, double x1, double y1, double x2, double y2, double inwardX = 0, double inwardY = 0)
    {
        using var paint = CreateBorderPaint(side);
        foreach (var stroke in BorderStrokeGeometry.GetStrokes(side, x1, y1, x2, y2, inwardX, inwardY))
        {
            canvas.DrawLine((float)stroke.X1, (float)stroke.Y1, (float)stroke.X2, (float)stroke.Y2, paint);
        }
    }

    private SKPaint CreateBorderPaint(BorderSide side)
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

    private SKPaint CreatePaint(ReportColor color, SKPaintStyle style, double width = 1) => new()
    {
        Color = new(color.Red, color.Green, color.Blue, color.Alpha),
        Style = style,
        StrokeWidth = (float)width,
        IsAntialias = true,
    };
}
