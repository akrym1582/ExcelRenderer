using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ReportEngine.Abstractions;
using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;
using SkiaSharp;

namespace ReportEngine.PdfSharp;

public sealed class PdfSharpRenderer : IRenderer
{
    public void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output)
    {
        using var document = new PdfDocument();
        var pages = commands.GroupBy(x => x.PageNumber).OrderBy(x => x.Key).ToArray();
        if (pages.Length == 0)
            AddPage(document, pageSettings, []);

        foreach (var pageCommands in pages)
            AddPage(document, pageSettings, pageCommands);
        document.Save(output, false);
    }

    private static void AddPage(PdfDocument document, PageSettings pageSettings, IEnumerable<DrawCommand> commands)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageSettings.Width);
        page.Height = XUnit.FromPoint(pageSettings.Height);
        using var graphics = XGraphics.FromPdfPage(page);
        foreach (var command in commands)
            Execute(graphics, command);
    }

    private static void Execute(XGraphics graphics, DrawCommand command)
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
                DrawText(graphics, text);
                break;
            case DrawLineCommand line:
                graphics.DrawLine(new XPen(ToColor(line.Style.Color ?? new(0, 0, 0)), line.Style.Width),
                    line.X1, line.Y1, line.X2, line.Y2);
                break;
            case DrawImageCommand image:
                DrawImage(graphics, image);
                break;
        }
    }

    private static void DrawText(XGraphics graphics, DrawTextCommand command)
    {
        var font = CreateFontToFit(graphics, command.Text, command.Style, command.Bounds.Width);
        var lines = WrapText(graphics, command.Text, font, command.Bounds.Width, command.Style.WrapText);
        var lineHeight = graphics.MeasureString("Ag", font).Height;
        var textHeight = lineHeight * lines.Count;
        var y = command.Style.VerticalAlignment switch
        {
            VerticalAlignment.Center => command.Bounds.Y + (command.Bounds.Height - textHeight) / 2,
            VerticalAlignment.Bottom => command.Bounds.Y + command.Bounds.Height - textHeight,
            _ => command.Bounds.Y
        };
        var state = graphics.Save();
        if (command.Style.WrapText || command.Style.ShrinkToFit)
            graphics.IntersectClip(ToRect(command.Bounds));
        var format = ToFormat(command.Style);
        format.LineAlignment = XLineAlignment.Near;
        var brush = new XSolidBrush(ToColor(command.Style.Font.Color ?? new(0, 0, 0)));
        foreach (var line in lines)
        {
            graphics.DrawString(line, font, brush, new XRect(command.Bounds.X, y, command.Bounds.Width, lineHeight), format);
            y += lineHeight;
        }
        graphics.Restore(state);
    }

    private static XFont CreateFontToFit(XGraphics graphics, string text, CellStyle style, double width)
    {
        var font = PdfSharpTextMeasurer.CreateFont(style.Font);
        if (!style.ShrinkToFit || style.WrapText || width <= 0) return font;

        var widestLine = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n').Max(line => graphics.MeasureString(line, font).Width);
        if (widestLine <= width) return font;

        return PdfSharpTextMeasurer.CreateFont(style.Font with
        {
            Size = style.Font.Size * width / widestLine
        });
    }

    private static IReadOnlyList<string> WrapText(XGraphics graphics, string text, XFont font, double width, bool wrap)
    {
        if (!wrap || width <= 0) return text.Split('\n');

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
                if (line.Length > 0 && graphics.MeasureString(candidate, font).Width > width)
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

    private static void DrawImage(XGraphics graphics, DrawImageCommand command)
    {
        using var bitmap = SKBitmap.Decode(command.ImageBytes);
        if (bitmap is null)
            throw new InvalidDataException("画像データを読み込めません。");

        using var image = SKImage.FromBitmap(bitmap);
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = pngData.AsStream();
        using var pdfImage = XImage.FromStream(stream);
        graphics.DrawImage(pdfImage, ToRect(command.Bounds));
    }

    private static void DrawBorder(XGraphics graphics, DrawBorderCommand command)
    {
        var rect = command.Bounds;
        DrawSide(command.Border.Top, rect.X, rect.Y, rect.X + rect.Width, rect.Y);
        DrawSide(command.Border.Right, rect.X + rect.Width, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
        DrawSide(command.Border.Bottom, rect.X, rect.Y + rect.Height, rect.X + rect.Width, rect.Y + rect.Height);
        DrawSide(command.Border.Left, rect.X, rect.Y, rect.X, rect.Y + rect.Height);

        void DrawSide(BorderSide? side, double x1, double y1, double x2, double y2)
        {
            if (side is not null)
                graphics.DrawLine(new XPen(ToColor(side.Color ?? new(0, 0, 0)), side.Width), x1, y1, x2, y2);
        }
    }

    private static XRect ToRect(ReportRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private static XColor ToColor(ReportColor color) => XColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    private static XStringFormat ToFormat(CellStyle style) => new()
    {
        Alignment = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => XStringAlignment.Center,
            HorizontalAlignment.Right => XStringAlignment.Far,
            _ => XStringAlignment.Near
        },
        LineAlignment = style.VerticalAlignment switch
        {
            VerticalAlignment.Center => XLineAlignment.Center,
            VerticalAlignment.Bottom => XLineAlignment.Far,
            _ => XLineAlignment.Near
        }
    };
}
