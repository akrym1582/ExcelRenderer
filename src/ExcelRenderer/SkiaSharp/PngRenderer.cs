using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>描画コマンドをページごとの PNG 画像として出力します。</summary>
public sealed class PngRenderer
{
    public const double DefaultDpi = 96;

    /// <summary>すべてのページを、ページ番号から出力先を作るファクトリを使って出力します。</summary>
    public void Render(
        IReadOnlyList<DrawCommand> commands,
        PageSettings pageSettings,
        Func<int, Stream> outputFactory,
        double dpi = DefaultDpi)
    {
        if (outputFactory is null) throw new ArgumentNullException(nameof(outputFactory));
        ValidateDpi(dpi);

        var pages = commands.GroupBy(command => command.PageNumber).OrderBy(page => page.Key).ToArray();
        if (pages.Length == 0)
        {
            using var output = outputFactory(1) ?? throw new InvalidOperationException("PNG の出力先を取得できません。");
            RenderPage([], pageSettings, output, dpi);
            return;
        }

        foreach (var page in pages)
        {
            using var output = outputFactory(page.Key) ?? throw new InvalidOperationException("PNG の出力先を取得できません。");
            RenderPage(page, pageSettings, output, dpi);
        }
    }

    /// <summary>指定した 1 ページ分の描画コマンドを PNG として出力します。</summary>
    public void RenderPage(
        IEnumerable<DrawCommand> commands,
        PageSettings pageSettings,
        Stream output,
        double dpi = DefaultDpi)
    {
        if (commands is null) throw new ArgumentNullException(nameof(commands));
        if (output is null) throw new ArgumentNullException(nameof(output));
        ValidateDpi(dpi);

        var scale = (float)(dpi / 72d);
        var width = Math.Max(1, (int)Math.Ceiling(pageSettings.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(pageSettings.Height * scale));
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);

        foreach (var command in commands)
            Execute(canvas, command);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(output);
    }

    private static void Execute(SKCanvas canvas, DrawCommand command)
    {
        switch (command)
        {
            case FillRectangleCommand fill:
                using (var paint = CreatePaint(fill.Color, SKPaintStyle.Fill))
                    canvas.DrawRect(ToRect(fill.Bounds), paint);
                break;
            case DrawBorderCommand border:
                DrawBorder(canvas, border);
                break;
            case DrawTextCommand text:
                DrawText(canvas, text);
                break;
            case DrawLineCommand line:
                using (var paint = CreatePaint(line.Style.Color ?? new(0, 0, 0), SKPaintStyle.Stroke, line.Style.Width))
                    canvas.DrawLine((float)line.X1, (float)line.Y1, (float)line.X2, (float)line.Y2, paint);
                break;
            case DrawImageCommand image:
                DrawImage(canvas, image);
                break;
        }
    }

    private static void DrawText(SKCanvas canvas, DrawTextCommand command)
    {
        using var typeface = SKTypeface.FromFamilyName(command.Style.Font.Family,
            command.Style.Font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            command.Style.Font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, (float)command.Style.Font.Size);
        using var paint = CreatePaint(command.Style.Font.Color ?? new(0, 0, 0), SKPaintStyle.Fill);
        paint.IsAntialias = true;

        if (command.Style.ShrinkToFit && !command.Style.WrapText && command.Bounds.Width > 0)
        {
            var widest = command.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n').Max(line => font.MeasureText(line, paint));
            if (widest > command.Bounds.Width)
                font.Size *= (float)(command.Bounds.Width / widest);
        }

        var lines = WrapText(command.Text, font, paint, command.Bounds.Width, command.Style.WrapText);
        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var textHeight = lineHeight * lines.Count;
        var y = command.Style.VerticalAlignment switch
        {
            VerticalAlignment.Center => (float)(command.Bounds.Y + (command.Bounds.Height - textHeight) / 2) - metrics.Ascent,
            VerticalAlignment.Bottom => (float)(command.Bounds.Y + command.Bounds.Height - textHeight) - metrics.Ascent,
            _ => (float)command.Bounds.Y - metrics.Ascent
        };

        canvas.Save();
        if (command.Style.WrapText || command.Style.ShrinkToFit)
            canvas.ClipRect(ToRect(command.Bounds));
        foreach (var line in lines)
        {
            var lineWidth = font.MeasureText(line, paint);
            var x = command.Style.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (float)(command.Bounds.X + (command.Bounds.Width - lineWidth) / 2),
                HorizontalAlignment.Right => (float)(command.Bounds.X + command.Bounds.Width - lineWidth),
                _ => (float)command.Bounds.X
            };
            canvas.DrawText(line, x, y, SKTextAlign.Left, font, paint);
            if (command.Style.Font.Underline)
                canvas.DrawLine(x, y + 1, x + lineWidth, y + 1, paint);
            y += lineHeight;
        }
        canvas.Restore();
    }

    private static IReadOnlyList<string> WrapText(string text, SKFont font, SKPaint paint, double width, bool wrap)
    {
        if (!wrap || width <= 0)
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = string.Empty;
            foreach (var character in paragraph)
            {
                var candidate = line + character;
                if (line.Length > 0 && font.MeasureText(candidate, paint) > width)
                {
                    lines.Add(line);
                    line = character.ToString();
                }
                else
                    line = candidate;
            }
            lines.Add(line);
        }
        return lines;
    }

    private static void DrawImage(SKCanvas canvas, DrawImageCommand command)
    {
        using var image = SKImage.FromEncodedData(command.ImageBytes)
            ?? throw new InvalidDataException("画像データを読み込めません。");
        canvas.DrawImage(image, ToRect(command.Bounds), new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    private static void DrawBorder(SKCanvas canvas, DrawBorderCommand command)
    {
        var rect = command.Bounds;
        DrawSide(command.Border.Top, rect.X, rect.Y, rect.X + rect.Width, rect.Y);
        DrawSide(command.Border.Right, rect.X + rect.Width, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
        DrawSide(command.Border.Bottom, rect.X, rect.Y + rect.Height, rect.X + rect.Width, rect.Y + rect.Height);
        DrawSide(command.Border.Left, rect.X, rect.Y, rect.X, rect.Y + rect.Height);

        void DrawSide(BorderSide? side, double x1, double y1, double x2, double y2)
        {
            if (side is null) return;
            using var paint = CreatePaint(side.Color ?? new(0, 0, 0), SKPaintStyle.Stroke, side.Width);
            canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, paint);
        }
    }

    private static SKPaint CreatePaint(ReportColor color, SKPaintStyle style, double width = 1) => new()
    {
        Color = new SKColor(color.Red, color.Green, color.Blue, color.Alpha),
        Style = style,
        StrokeWidth = (float)width,
        IsAntialias = true
    };

    private static SKRect ToRect(ReportRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

    private static void ValidateDpi(double dpi)
    {
        if (double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI は 0 より大きい有限値で指定してください。");
    }
}
