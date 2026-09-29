using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>PNG と SVG で共有する SkiaSharp 描画コマンド実行処理です。</summary>
internal sealed class SkiaDrawingContext
{
    private readonly bool _textAsPaths;
    private readonly IFontManager? _fontManager;

    /// <summary>Initializes a new instance of the <see cref="SkiaDrawingContext"/> class. PNG または SVG の描画先へコマンドを実行するコンテキストを初期化します。</summary>
    /// <param name="textAsPaths">true の場合は文字をパスとして描画します。</param>
    /// <param name="fontManager">文字の描画に使用するフォントを解決するマネージャーです。指定しない場合は内蔵フォントを使用します。</param>
    internal SkiaDrawingContext(bool textAsPaths, IFontManager? fontManager = null)
    {
        _textAsPaths = textAsPaths;
        _fontManager = fontManager;
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
                DrawText(canvas, text);
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

    private static SKTypeface? CreateTypeface(ResolvedFont font)
    {
        using Stream stream = font.FontData is null
            ? File.OpenRead(font.FilePath)
            : new MemoryStream(font.FontData, writable: false);
        return SKTypeface.FromStream(stream);
    }

    private static SKRect ToRect(ReportRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

    private void DrawShape(SKCanvas canvas, DrawShapeCommand command)
    {
        var b = ToRect(command.Bounds);
        canvas.Save();
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
            DrawText(canvas, new DrawTextCommand(command.PageNumber, bounds, text.Text, CellStyle.Default with
            { Font = text.Font, HorizontalAlignment = text.HorizontalAlignment, VerticalAlignment = text.VerticalAlignment, WrapText = text.WrapText }));
        }

        canvas.Restore();
    }

    private void DrawText(SKCanvas canvas, DrawTextCommand command)
    {
        var request = new FontRequest(
            command.Style.Font.Family,
            command.Style.Font.Bold ? 700 : 400,
            command.Style.Font.Italic);
        var resolved = _fontManager?.Resolve(request);
        using var resolvedTypeface = resolved is null ? null : CreateTypeface(resolved);
        using var systemTypeface = resolvedTypeface is null
            ? SKTypeface.FromFamilyName(
                command.Style.Font.Family,
                command.Style.Font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                command.Style.Font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
            : null;
        var typeface = resolvedTypeface ?? systemTypeface ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (float)command.Style.Font.Size);
        using var paint = CreatePaint(command.Style.Font.Color ?? new(0, 0, 0), SKPaintStyle.Fill);
        paint.IsAntialias = true;

        if (command.Style.ShrinkToFit && !command.Style.WrapText && command.Bounds.Width > 0)
        {
            var widest = command.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n').Max(line => MeasureText(line, request, font, paint));
            if (widest > command.Bounds.Width)
            {
                font.Size *= (float)(command.Bounds.Width / widest);
            }
        }

        var lines = WrapText(command.Text, request, font, paint, command.Bounds.Width, command.Style.WrapText);
        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var textHeight = lineHeight * lines.Count;
        var y = command.Style.VerticalAlignment switch
        {
            VerticalAlignment.Center => (float)(command.Bounds.Y + ((command.Bounds.Height - textHeight) / 2)) - metrics.Ascent,
            VerticalAlignment.Bottom => (float)(command.Bounds.Y + command.Bounds.Height - textHeight) - metrics.Ascent,
            _ => (float)command.Bounds.Y - metrics.Ascent,
        };

        canvas.Save();
        if (command.Style.WrapText || command.Style.ShrinkToFit)
        {
            canvas.ClipRect(ToRect(command.Bounds));
        }

        foreach (var line in lines)
        {
            var lineWidth = MeasureText(line, request, font, paint);
            var x = command.Style.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (float)(command.Bounds.X + ((command.Bounds.Width - lineWidth) / 2)),
                HorizontalAlignment.Right => (float)(command.Bounds.X + command.Bounds.Width - lineWidth),
                _ => (float)command.Bounds.X,
            };
            if (_textAsPaths)
            {
                DrawResolvedLine(canvas, line, request, font.Size, x, y, paint, asPaths: true);
            }
            else
            {
                DrawResolvedLine(canvas, line, request, font.Size, x, y, paint, asPaths: false);
            }

            if (command.Style.Font.Underline)
            {
                canvas.DrawLine(x, y + 1, x + lineWidth, y + 1, paint);
            }

            y += lineHeight;
        }

        canvas.Restore();
    }

    private IReadOnlyList<string> WrapText(string text, FontRequest request, SKFont font, SKPaint paint, double width, bool wrap)
    {
        if (!wrap || width <= 0)
        {
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        }

        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = string.Empty;
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(paragraph);
            while (elements.MoveNext())
            {
                var element = (string)elements.Current!;
                var candidate = line + element;
                if (line.Length > 0 && MeasureText(candidate, request, font, paint) > width)
                {
                    lines.Add(line);
                    line = element;
                }
                else
                {
                    line = candidate;
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    private float MeasureText(string text, FontRequest request, SKFont defaultFont, SKPaint paint)
    {
        if (_fontManager is null)
        {
            return defaultFont.MeasureText(text, paint);
        }

        var width = 0f;
        foreach (var run in _fontManager.ResolveTextRuns(text, request))
        {
            using var typeface = CreateTypeface(run.Font);
            using var font = new SKFont(typeface, defaultFont.Size);
            width += MeasureRun(font, run, paint);
        }

        return width;
    }

    private void DrawResolvedLine(SKCanvas canvas, string text, FontRequest request, float size, float x, float y, SKPaint paint, bool asPaths)
    {
        var runs = _fontManager?.ResolveTextRuns(text, request);
        if (runs is null)
        {
            runs = [new(text, new ResolvedFont(string.Empty, request.Weight, request.Italic, string.Empty))];
        }

        foreach (var run in runs)
        {
            using var typeface = string.IsNullOrEmpty(run.Font.FilePath) ? null : CreateTypeface(run.Font);
            using var font = new SKFont(typeface ?? SKTypeface.Default, size);
            if (run.GlyphId is { } glyphId)
            {
                using var glyphPath = font.GetGlyphPath(glyphId)
                    ?? throw new InvalidOperationException($"IVS glyph {glyphId} のアウトラインを生成できません。");
                if (glyphPath.IsEmpty)
                {
                    throw new InvalidOperationException($"IVS glyph {glyphId} のアウトラインが空です。");
                }

                glyphPath.Transform(SKMatrix.CreateTranslation(x, y));
                canvas.DrawPath(glyphPath, paint);
            }
            else if (asPaths)
            {
                using var path = font.GetTextPath(run.Text, new SKPoint(x, y));
                if (path.IsEmpty && run.Text.Any(character => !char.IsWhiteSpace(character)))
                {
                    throw new InvalidOperationException("文字のアウトラインを生成できません。");
                }

                canvas.DrawPath(path, paint);
            }
            else
            {
                canvas.DrawText(run.Text, x, y, SKTextAlign.Left, font, paint);
            }

            x += MeasureRun(font, run, paint);
        }
    }

    private static float MeasureRun(SKFont font, TextRun run, SKPaint paint)
    {
        if (run.GlyphId is not { } glyphId)
        {
            return font.MeasureText(run.Text, paint);
        }

        var widths = font.GetGlyphWidths([glyphId]);
        return widths.Length == 0 ? 0 : widths[0];
    }

    private void DrawImage(SKCanvas canvas, DrawImageCommand command)
    {
        using var image = SKImage.FromEncodedData(command.ImageBytes)
            ?? throw new InvalidDataException("画像データを読み込めません。");
        canvas.DrawImage(image, ToRect(command.Bounds), new SKSamplingOptions(SKCubicResampler.Mitchell));
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
        Color = new SKColor(color.Red, color.Green, color.Blue, color.Alpha),
        Style = style,
        StrokeWidth = (float)width,
        IsAntialias = true,
    };
}
