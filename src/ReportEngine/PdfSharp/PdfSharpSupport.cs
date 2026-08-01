using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ReportEngine.Abstractions;
using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.PdfSharp;

public sealed class PdfSharpTextMeasurer : ITextMeasurer
{
    public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap)
    {
        if (string.IsNullOrEmpty(text)) return new(0, 0);
        using var graphics = XGraphics.CreateMeasureContext(new XSize(availableWidth, double.MaxValue), XGraphicsUnit.Point, XPageDirection.Downwards);
        var size = graphics.MeasureString(text, CreateFont(font));
        if (!wrap || size.Width <= availableWidth) return new(size.Width, size.Height);
        var lines = Math.Ceiling(size.Width / Math.Max(availableWidth, 1));
        return new(availableWidth, size.Height * lines);
    }

    internal static XFont CreateFont(FontStyle font)
    {
        var style = XFontStyleEx.Regular;
        if (font.Bold) style |= XFontStyleEx.Bold;
        if (font.Italic) style |= XFontStyleEx.Italic;
        if (font.Underline) style |= XFontStyleEx.Underline;
        return new XFont(font.Family, font.Size, style);
    }
}

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
                graphics.DrawString(text.Text, PdfSharpTextMeasurer.CreateFont(text.Style.Font),
                    new XSolidBrush(ToColor(text.Style.Font.Color ?? new(0, 0, 0))), ToRect(text.Bounds), ToFormat(text.Style));
                break;
            case DrawLineCommand line:
                graphics.DrawLine(new XPen(ToColor(line.Style.Color ?? new(0, 0, 0)), line.Style.Width),
                    line.X1, line.Y1, line.X2, line.Y2);
                break;
            case DrawImageCommand:
                throw new NotSupportedException("Image rendering is not implemented in the MVP.");
        }
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
