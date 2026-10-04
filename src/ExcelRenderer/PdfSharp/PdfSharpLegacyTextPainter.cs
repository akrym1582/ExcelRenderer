using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using PdfSharp.Drawing;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

/// <summary>Owns PDF compatibility measurement, wrapping, fallback, IVS, and emoji drawing.</summary>
internal sealed class PdfSharpLegacyTextPainter
{
    private readonly IFontManager? _fontManager;
    private readonly Action? _beforeResolvedDrawing;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpLegacyTextPainter"/> class.</summary>
    /// <param name="fontManager">The optional resolved-font source.</param>
    /// <param name="beforeResolvedDrawing">Optional internal observer after clipping and measurement, before resolved run drawing.</param>
    internal PdfSharpLegacyTextPainter(IFontManager? fontManager, Action? beforeResolvedDrawing = null)
    {
        _fontManager = fontManager;
        _beforeResolvedDrawing = beforeResolvedDrawing;
    }

    /// <summary>Measures and paints a compatibility command.</summary>
    /// <param name="graphics">The caller-owned PDF graphics target.</param>
    /// <param name="command">The compatibility text command.</param>
    internal void Paint(XGraphics graphics, DrawTextCommand command)
    {
        var request = ToRequest(command.Style);
        if (_fontManager is not null && RequiresResolvedTextPath(
            _fontManager.ResolveTextRuns(command.Text, request), _fontManager.Resolve(request), request))
        {
            DrawTextWithIvs(graphics, command);
            return;
        }

        var font = CreateFontToFit(graphics, command.Text, command.Style, command.Bounds.Width);
        var lines = WrapText(graphics, command.Text, font, command.Bounds.Width, command.Style.WrapText);
        var lineHeight = graphics.MeasureString("Ag", font).Height;
        var textHeight = lineHeight * lines.Count;
        var y = command.Style.VerticalAlignment switch
        {
            VerticalAlignment.Center => command.Bounds.Y + ((command.Bounds.Height - textHeight) / 2),
            VerticalAlignment.Bottom => command.Bounds.Y + command.Bounds.Height - textHeight,
            _ => command.Bounds.Y,
        };
        var state = graphics.Save();
        try
        {
            if (command.Style.WrapText || command.Style.ShrinkToFit)
            {
                graphics.IntersectClip(ToRect(command.Bounds));
            }

            var format = ToFormat(command.Style);
            format.LineAlignment = XLineAlignment.Near;
            var brush = new XSolidBrush(ToColor(command.Style.Font.Color ?? new(0, 0, 0)));
            for (var index = 0; index < lines.Count; index++)
            {
                graphics.DrawString(lines[index], font, brush, new XRect(command.Bounds.X, y, command.Bounds.Width, lineHeight), format);
                y += lineHeight;
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static bool RequiresResolvedTextPath(IReadOnlyList<TextRun> runs, ResolvedFont primary, FontRequest request) =>
        runs.Any(x => x.GlyphId is not null || x.ColorEmojiGlyphId is not null ||
            x.MissingPrivateUseGlyph || x.Font.FaceId != primary.FaceId) ||
        !string.Equals(primary.Family, request.Family, StringComparison.OrdinalIgnoreCase);

    private static FontRequest ToRequest(CellStyle style) => new(style.Font.Family, style.Font.Bold ? 700 : 400, style.Font.Italic);

    private static SKTypeface CreateTypeface(ResolvedFont font)
    {
        using Stream stream = font.FontData is null ? File.OpenRead(font.FilePath) : new MemoryStream(font.FontData, false);
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"フォント {font.Family} を読み込めません。");
    }

    private static XGraphicsPath ToPdfPath(SKPath source, double offsetX, double offsetY)
    {
        var result = new XGraphicsPath { FillMode = XFillMode.Winding };
        using var iterator = source.CreateRawIterator();
        var points = new SKPoint[4];
        while (true)
        {
            var verb = iterator.Next(points);
            XPoint P(int index) => new(offsetX + points[index].X, offsetY + points[index].Y);
            if (verb == SKPathVerb.Done)
            {
                break;
            }

            switch (verb)
            {
                case SKPathVerb.Move: result.StartFigure(); break;
                case SKPathVerb.Line: result.AddLine(P(0), P(1)); break;
                case SKPathVerb.Quad:
                    var p0 = P(0); var p1 = P(1); var p2 = P(2);
                    result.AddBezier(
                        p0,
                        new(p0.X + ((p1.X - p0.X) * 2 / 3), p0.Y + ((p1.Y - p0.Y) * 2 / 3)),
                        new(p2.X + ((p1.X - p2.X) * 2 / 3), p2.Y + ((p1.Y - p2.Y) * 2 / 3)),
                        p2);
                    break;
                case SKPathVerb.Cubic: result.AddBezier(P(0), P(1), P(2), P(3)); break;
                case SKPathVerb.Close: result.CloseFigure(); break;
                case SKPathVerb.Conic: throw new InvalidOperationException("Conic IVS glyph paths are not supported by PDFsharp.");
            }
        }

        return result;
    }

    private static XFont CreateFontToFit(XGraphics graphics, string text, CellStyle style, double width)
    {
        var font = PdfSharpTextMeasurer.CreateFont(style.Font);
        if (!style.ShrinkToFit || style.WrapText || width <= 0)
        {
            return font;
        }

        var widestLine = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n').Max(line => graphics.MeasureString(line, font).Width);
        if (widestLine <= width)
        {
            return font;
        }

        return PdfSharpTextMeasurer.CreateFont(style.Font with
        {
            Size = style.Font.Size * width / widestLine,
        });
    }

    private static IReadOnlyList<string> WrapText(XGraphics graphics, string text, XFont font, double width, bool wrap)
    {
        if (!wrap || width <= 0)
        {
            return text.Split('\n');
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
                if (line.Length > 0 && graphics.MeasureString(candidate, font).Width > width)
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

    private static XRect ToRect(ReportRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static XColor ToColor(ReportColor color) => XColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    private static XStringFormat ToFormat(CellStyle style) => new()
    {
        Alignment = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => XStringAlignment.Center,
            HorizontalAlignment.Right => XStringAlignment.Far,
            _ => XStringAlignment.Near,
        },
        LineAlignment = style.VerticalAlignment switch
        {
            VerticalAlignment.Center => XLineAlignment.Center,
            VerticalAlignment.Bottom => XLineAlignment.Far,
            _ => XLineAlignment.Near,
        },
    };

    private void DrawTextWithIvs(XGraphics graphics, DrawTextCommand command)
    {
        var request = ToRequest(command.Style);
        var size = command.Style.Font.Size;
        var lines = command.TextLayout?.Lines.Select(line => line.Text).ToArray() ??
            WrapResolvedText(graphics, command.Text, request, size, command.Bounds.Width, command.Style.WrapText);
        if (command.Style.ShrinkToFit && !command.Style.WrapText && command.Bounds.Width > 0)
        {
            var widest = lines.Max(x => MeasureResolvedText(graphics, x, request, size));
            if (widest > command.Bounds.Width)
            {
                size *= command.Bounds.Width / widest;
            }
        }

        using var metricsTypeface = CreateTypeface(_fontManager!.Resolve(request));
        using var metricsFont = new SKFont(metricsTypeface, (float)size);
        var metrics = metricsFont.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var textHeight = lineHeight * lines.Count;
        var y = command.Style.VerticalAlignment switch
        {
            VerticalAlignment.Center => command.Bounds.Y + ((command.Bounds.Height - textHeight) / 2),
            VerticalAlignment.Bottom => command.Bounds.Y + command.Bounds.Height - textHeight,
            _ => command.Bounds.Y,
        };
        var state = graphics.Save();
        try
        {
            if (command.Style.WrapText || command.Style.ShrinkToFit)
            {
                graphics.IntersectClip(ToRect(command.Bounds));
            }

            var brush = new XSolidBrush(ToColor(command.Style.Font.Color ?? new(0, 0, 0)));
            foreach (var line in lines)
            {
                var lineWidth = MeasureResolvedText(graphics, line, request, size);
                var x = command.Style.HorizontalAlignment switch
                {
                    HorizontalAlignment.Center => command.Bounds.X + ((command.Bounds.Width - lineWidth) / 2),
                    HorizontalAlignment.Right => command.Bounds.X + command.Bounds.Width - lineWidth,
                    _ => command.Bounds.X,
                };
                _beforeResolvedDrawing?.Invoke();
                foreach (var run in _fontManager.ResolveTextRuns(line, request))
                {
                    if (run.ColorEmojiGlyphId is { } emojiGlyph)
                    {
                        using var bitmap = ColorEmojiBitmap.Create(run.Font, emojiGlyph, (float)size);
                        using var png = bitmap.Image.Encode(SKEncodedImageFormat.Png, 100);
                        using var stream = png.AsStream();
                        using var image = XImage.FromStream(stream);
                        graphics.DrawImage(
                            image,
                            new XRect(
                                x + bitmap.Bounds.Left,
                                y - metrics.Ascent + bitmap.Bounds.Top,
                                bitmap.Bounds.Width,
                                bitmap.Bounds.Height));
                        using var emojiTypeface = CreateTypeface(run.Font);
                        using var emojiFont = new SKFont(emojiTypeface, (float)size);
                        x += emojiFont.GetGlyphWidths([emojiGlyph])[0];
                    }
                    else if (run.GlyphId is { } glyph || run.MissingPrivateUseGlyph)
                    {
                        using var typeface = CreateTypeface(run.Font);
                        using var font = new SKFont(typeface, (float)size);
                        glyph = run.GlyphId ?? font.GetGlyphs(run.Text)[0];
                        using var path = font.GetGlyphPath(glyph)
                            ?? throw new InvalidOperationException($"Glyph {glyph} のアウトラインを生成できません。");
                        if (path.IsEmpty)
                        {
                            throw new InvalidOperationException($"Glyph {glyph} のアウトラインが空です。");
                        }

                        graphics.DrawPath(brush, ToPdfPath(path, x, y - metrics.Ascent));
                        x += font.GetGlyphWidths([glyph])[0];
                    }
                    else
                    {
                        var runFont = PdfSharpTextMeasurer.CreateFont(command.Style.Font with { Family = run.Font.Family, Size = size });
                        graphics.DrawString(run.Text, runFont, brush, new XPoint(x, y), XStringFormats.TopLeft);
                        x += graphics.MeasureString(run.Text, runFont).Width;
                    }
                }

                if (command.Style.Font.Underline)
                {
                    graphics.DrawLine(new XPen(brush.Color), command.Bounds.X, y + lineHeight - 1, command.Bounds.X + lineWidth, y + lineHeight - 1);
                }

                y += lineHeight;
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private IReadOnlyList<string> WrapResolvedText(XGraphics graphics, string text, FontRequest request, double size, double width, bool wrap)
    {
        if (!wrap || width <= 0)
        {
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        }

        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = string.Empty;
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(paragraph);
            while (elements.MoveNext())
            {
                var element = (string)elements.Current!;
                if (line.Length > 0 && MeasureResolvedText(graphics, line + element, request, size) > width)
                {
                    result.Add(line);
                    line = element;
                }
                else
                {
                    line += element;
                }
            }

            result.Add(line);
        }

        return result;
    }

    private double MeasureResolvedText(XGraphics graphics, string text, FontRequest request, double size)
    {
        var width = 0d;
        foreach (var run in _fontManager!.ResolveTextRuns(text, request))
        {
            if ((run.GlyphId ?? run.ColorEmojiGlyphId) is { } glyph || run.MissingPrivateUseGlyph)
            {
                using var typeface = CreateTypeface(run.Font);
                using var font = new SKFont(typeface, (float)size);
                glyph = run.GlyphId ?? run.ColorEmojiGlyphId ?? font.GetGlyphs(run.Text)[0];
                width += font.GetGlyphWidths([glyph])[0];
            }
            else
            {
                width += graphics.MeasureString(run.Text, PdfSharpTextMeasurer.CreateFont(new FontStyle(run.Font.Family, size))).Width;
            }
        }

        return width;
    }
}
