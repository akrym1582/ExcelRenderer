using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ReportEngine.Abstractions;
using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;
using SkiaSharp;

namespace ReportEngine.PdfSharp;

public sealed class PdfSharpFontResolver : IFontResolver
{
    private readonly string _familyName;
    private readonly string _faceName;
    private readonly byte[] _fontData;

    public PdfSharpFontResolver(string familyName, string fontFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFilePath);

        _familyName = familyName;
        _faceName = Path.GetFullPath(fontFilePath);
        _fontData = File.ReadAllBytes(_faceName);
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        string.Equals(familyName, _familyName, StringComparison.OrdinalIgnoreCase)
            ? new FontResolverInfo(_faceName)
            : null;

    public byte[]? GetFont(string faceName) =>
        string.Equals(faceName, _faceName, StringComparison.Ordinal)
            ? _fontData
            : null;
}

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
            case DrawImageCommand image:
                DrawImage(graphics, image);
                break;
        }
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
