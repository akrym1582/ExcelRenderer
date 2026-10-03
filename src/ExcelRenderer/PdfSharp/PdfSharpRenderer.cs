using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

/// <summary>ページ別の描画コマンドを PDFsharp で描画し、PDF 文書として出力します。</summary>
public sealed class PdfSharpRenderer : IRenderer
{
    private readonly IFontManager? _fontManager;
    private readonly PdfSharpTextPainter _textPainter;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpRenderer"/> class.</summary>
    public PdfSharpRenderer() => _textPainter = new(DrawFinalizedText, DrawLegacyText);

    /// <summary>Initializes a new instance of the <see cref="PdfSharpRenderer"/> class.</summary>
    /// <param name="fontManager">The font manager shared with layout and diagnostics.</param>
    public PdfSharpRenderer(IFontManager fontManager)
    {
        _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));
        _textPainter = new(DrawFinalizedText, DrawLegacyText);
    }

    /// <summary>描画コマンドをページ番号ごとに描画し、すべてのページを含む PDF 文書を出力します。</summary>
    /// <param name="commands">背景、罫線、文字、画像、および図形をページ上へ配置する描画コマンドです。</param>
    /// <param name="pageSettings">各 PDF ページに適用する幅と高さを含むページ設定です。</param>
    /// <param name="output">生成した PDF 文書を書き込むストリームです。</param>
    public void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output)
    {
        using var document = new PdfDocument();
        var pages = commands.GroupBy(x => x.PageNumber).OrderBy(x => x.Key).ToArray();
        if (pages.Length == 0)
        {
            AddPage(document, pageSettings, []);
        }

        foreach (var pageCommands in pages)
        {
            AddPage(document, pageSettings, pageCommands);
        }

        document.Save(output, false);
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

    private static void DrawImage(XGraphics graphics, DrawImageCommand command)
    {
        using var bitmap = SKBitmap.Decode(command.ImageBytes);
        if (bitmap is null)
        {
            throw new InvalidDataException("画像データを読み込めません。");
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = pngData.AsStream();
        using var pdfImage = XImage.FromStream(stream);
        var state = graphics.Save();
        if (command.ClipBounds is { } clip)
        {
            graphics.IntersectClip(ToRect(clip));
        }

        var centerX = command.Bounds.X + (command.Bounds.Width / 2);
        var centerY = command.Bounds.Y + (command.Bounds.Height / 2);
        graphics.TranslateTransform(centerX, centerY);
        graphics.RotateTransform(command.Rotation);
        graphics.ScaleTransform(command.FlipHorizontal ? -1 : 1, command.FlipVertical ? -1 : 1);
        graphics.TranslateTransform(-centerX, -centerY);
        if (command.Crop is { } crop)
        {
            var source = new XRect(
                crop.Left * pdfImage.PointWidth,
                crop.Top * pdfImage.PointHeight,
                Math.Max(0, 1 - crop.Left - crop.Right) * pdfImage.PointWidth,
                Math.Max(0, 1 - crop.Top - crop.Bottom) * pdfImage.PointHeight);
            graphics.DrawImage(pdfImage, ToRect(command.Bounds), source, XGraphicsUnit.Point);
        }
        else
        {
            graphics.DrawImage(pdfImage, ToRect(command.Bounds));
        }

        graphics.Restore(state);
    }

    private static void DrawBorder(XGraphics graphics, DrawBorderCommand command)
    {
        var rect = command.Bounds;
        DrawSide(command.Border.Top, rect.X, rect.Y, rect.X + rect.Width, rect.Y, 0, 1);
        DrawSide(command.Border.Right, rect.X + rect.Width, rect.Y, rect.X + rect.Width, rect.Y + rect.Height, -1, 0);
        DrawSide(command.Border.Bottom, rect.X, rect.Y + rect.Height, rect.X + rect.Width, rect.Y + rect.Height, 0, -1);
        DrawSide(command.Border.Left, rect.X, rect.Y, rect.X, rect.Y + rect.Height, 1, 0);

        void DrawSide(BorderSide? side, double x1, double y1, double x2, double y2, double inwardX, double inwardY)
        {
            if (side is not null)
            {
                DrawStyledLine(graphics, side, x1, y1, x2, y2, inwardX, inwardY);
            }
        }
    }

    private static void DrawStyledLine(XGraphics graphics, BorderSide side, double x1, double y1, double x2, double y2, double inwardX = 0, double inwardY = 0)
    {
        var pen = CreateBorderPen(side);
        foreach (var stroke in BorderStrokeGeometry.GetStrokes(side, x1, y1, x2, y2, inwardX, inwardY))
        {
            graphics.DrawLine(pen, stroke.X1, stroke.Y1, stroke.X2, stroke.Y2);
        }
    }

    private static XPen CreateBorderPen(BorderSide side)
    {
        var pen = new XPen(ToColor(side.Color ?? new(0, 0, 0)), side.Width);
        var pattern = side.LineStyle switch
        {
            BorderLineStyle.Dotted => new double[] { 1, 2 },
            BorderLineStyle.Dashed => new double[] { 3, 2 },
            BorderLineStyle.DashDot => new double[] { 3, 2, 1, 2 },
            BorderLineStyle.DashDotDot => new double[] { 3, 2, 1, 2, 1, 2 },
            _ => null,
        };
        if (pattern is not null)
        {
            pen.DashPattern = pattern;
        }

        return pen;
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

    private void AddPage(PdfDocument document, PageSettings pageSettings, IEnumerable<DrawCommand> commands)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageSettings.Width);
        page.Height = XUnit.FromPoint(pageSettings.Height);
        using var graphics = XGraphics.FromPdfPage(page);
        foreach (var command in commands)
        {
            Execute(graphics, command);
        }
    }

    private void Execute(XGraphics graphics, DrawCommand command)
    {
        switch (command)
        {
            case FillRectangleCommand fill:
                graphics.DrawRectangle(new XSolidBrush(ToColor(fill.Color)), ToRect(fill.Bounds));
                break;
            case DrawBorderCommand border:
                DrawBorder(graphics, border);
                break;
            case DrawTextCommand text:
                _textPainter.Paint(graphics, text);
                break;
            case DrawLineCommand line:
                DrawStyledLine(graphics, line.Style, line.X1, line.Y1, line.X2, line.Y2);
                break;
            case DrawImageCommand image:
                DrawImage(graphics, image);
                break;
            case DrawShapeCommand shape:
                DrawShape(graphics, shape);
                break;
        }
    }

    private void DrawShape(XGraphics graphics, DrawShapeCommand command)
    {
        var state = graphics.Save();
        var b = command.Bounds;
        if (command.ClipBounds is { } clip)
        {
            graphics.IntersectClip(ToRect(clip));
        }

        if (command.Shape.Rotation != 0)
        {
            graphics.RotateAtTransform(command.Shape.Rotation, new XPoint(b.X + (b.Width / 2), b.Y + (b.Height / 2)));
        }

        var brush = command.Shape.Style.FillColor is { } fill ? new XSolidBrush(ToColor(fill)) : null;
        var pen = command.Shape.Style.LineColor is { } line ? new XPen(ToColor(line), command.Shape.Style.LineWidth) : null;
        if (command.Shape.Kind == ShapeKind.Ellipse)
        {
            graphics.DrawEllipse(pen, brush, ToRect(b));
        }
        else if (command.Shape.Kind == ShapeKind.RoundedRectangle)
        {
            graphics.DrawRoundedRectangle(pen, brush, ToRect(b), new XSize(Math.Min(10, b.Width / 4), Math.Min(10, b.Height / 4)));
        }
        else if (command.Shape.Kind is ShapeKind.WedgeRectangleCallout or ShapeKind.WedgeRoundedRectangleCallout)
        {
            var path = new XGraphicsPath();
            path.AddPolygon([new(b.X, b.Y), new(b.X + b.Width, b.Y), new(b.X + b.Width, b.Y + b.Height),
                new(b.X + (b.Width * .35), b.Y + b.Height), new(b.X + (b.Width * .15), b.Y + (b.Height * 1.2)),
                new(b.X + (b.Width * .2), b.Y + b.Height), new(b.X, b.Y + b.Height)]);
            path.CloseFigure();
            graphics.DrawPath(pen, brush, path);
        }
        else
        {
            graphics.DrawRectangle(pen, brush, ToRect(b));
        }

        if (command.Shape.Text is { } text)
        {
            var bounds = new ReportRect(
                b.X + text.MarginLeft,
                b.Y + text.MarginTop,
                Math.Max(0, b.Width - text.MarginLeft - text.MarginRight),
                Math.Max(0, b.Height - text.MarginTop - text.MarginBottom));
            _textPainter.Paint(graphics, new DrawTextCommand(
                command.PageNumber,
                bounds,
                text.Text,
                CellStyle.Default with
                {
                    Font = text.Font,
                    HorizontalAlignment = text.HorizontalAlignment,
                    VerticalAlignment = text.VerticalAlignment,
                    WrapText = text.WrapText,
                }));
        }

        graphics.Restore(state);
    }

    private void DrawLegacyText(XGraphics graphics, DrawTextCommand command)
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

        graphics.Restore(state);
    }

    private void DrawFinalizedText(XGraphics graphics, DrawTextCommand command, TextLayoutResult layout)
    {
        var effectiveFontSize = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (effectiveFontSize == 0)
        {
            return;
        }

        var state = graphics.Save();
        if (command.Style.WrapText || command.Style.ShrinkToFit)
        {
            graphics.IntersectClip(ToRect(command.Bounds));
        }

        var brush = new XSolidBrush(ToColor(command.Style.Font.Color ?? new(0, 0, 0)));
        foreach (var positioned in TextLayoutPlacement.Place(
                     layout,
                     command.Bounds,
                     command.Style.HorizontalAlignment,
                     command.Style.VerticalAlignment))
        {
            if (positioned.Line.Runs.Count == 0)
            {
                var font = PdfSharpTextMeasurer.CreateFont(
                    command.Style.Font with { Size = effectiveFontSize },
                    includeUnderline: false);
                graphics.DrawString(
                    positioned.Line.Text,
                    font,
                    brush,
                    new XPoint(positioned.Left, positioned.Baseline),
                    XStringFormats.BaseLineLeft);
            }
            else
            {
                foreach (var run in positioned.Line.Runs)
                {
                    DrawFinalizedRun(
                        graphics,
                        run.Run,
                        effectiveFontSize,
                        positioned.Left + run.X,
                        positioned.Baseline,
                        command.Style,
                        brush);
                }
            }

            if (command.Style.Font.Underline)
            {
                graphics.DrawLine(
                    new XPen(brush.Color),
                    positioned.Left,
                    positioned.Baseline + 1,
                    positioned.Left + positioned.Line.Width,
                    positioned.Baseline + 1);
            }
        }

        graphics.Restore(state);
    }

    private void DrawFinalizedRun(
        XGraphics graphics,
        TextRun run,
        double size,
        double x,
        double baseline,
        CellStyle style,
        XSolidBrush brush)
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
                    baseline + bitmap.Bounds.Top,
                    bitmap.Bounds.Width,
                    bitmap.Bounds.Height));
            return;
        }

        if (run.GlyphId is { } glyph || run.MissingPrivateUseGlyph)
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

            graphics.DrawPath(brush, ToPdfPath(path, x, baseline));
            return;
        }

        var runFont = PdfSharpTextMeasurer.CreateResolvedFont(run.Font, size);
        graphics.DrawString(run.Text, runFont, brush, new XPoint(x, baseline), XStringFormats.BaseLineLeft);
    }

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

        graphics.Restore(state);
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
