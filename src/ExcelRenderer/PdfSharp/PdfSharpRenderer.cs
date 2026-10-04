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
    private readonly PdfSharpTextPainter _textPainter;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpRenderer"/> class.</summary>
    public PdfSharpRenderer() => _textPainter = new(null);

    /// <summary>Initializes a new instance of the <see cref="PdfSharpRenderer"/> class.</summary>
    /// <param name="fontManager">The font manager shared with layout and diagnostics.</param>
    public PdfSharpRenderer(IFontManager fontManager)
    {
        _textPainter = new(fontManager ?? throw new ArgumentNullException(nameof(fontManager)));
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
}
