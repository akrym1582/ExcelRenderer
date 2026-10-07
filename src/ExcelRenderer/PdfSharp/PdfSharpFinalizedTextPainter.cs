using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using PdfSharp.Drawing;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

/// <summary>Draws finalized PDF lines at their stored point-coordinate baselines and run offsets.</summary>
internal sealed class PdfSharpFinalizedTextPainter
{
    /// <summary>Paints stored lines and runs without changing finalized geometry.</summary>
    /// <param name="graphics">The caller-owned PDF graphics target.</param>
    /// <param name="command">The finalized text command.</param>
    /// <param name="layout">The finalized point-coordinate layout.</param>
    internal void Paint(XGraphics graphics, DrawTextCommand command, TextLayoutResult layout)
    {
        var effectiveFontSize = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (effectiveFontSize == 0)
        {
            return;
        }

        var state = graphics.Save();
        try
        {
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
        }
        finally
        {
            graphics.Restore(state);
        }
    }

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

    private static XRect ToRect(ReportRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static XColor ToColor(ReportColor color) => XColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

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
            using var ownedTypeface = ConversionFontResources.Current is null ? CreateTypeface(run.Font) : null;
            var typeface = ConversionFontResources.Current?.GetTypeface(run.Font) ?? ownedTypeface!;
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
}
