using System.Diagnostics;
using System.Globalization;
using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;
using RenderingDiagnostic = ExcelRenderer.Core.Rendering.ConversionDiagnostic;

namespace ExcelRenderer.Core.Pdf;

/// <summary>ページ別の描画コマンドを PDFsharp で描画し、PDF 文書として出力します。</summary>
internal class CorePdfRenderer
{
    private readonly Extensibility.ICorePdfTextPainter? textPainter;
    private readonly bool clipPage;

    /// <summary>Initializes a new instance of the <see cref="CorePdfRenderer"/> class.</summary>
    /// <param name="context">The shared single font; may be null for image-only tests.</param>
    internal CorePdfRenderer(SingleFontContext? context = null)
        : this(context is null ? null : new PdfSharpFinalizedTextPainter(context), true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CorePdfRenderer"/> class with a session painter.</summary>
    /// <param name="textPainter">The painter fixed for this renderer.</param>
    /// <param name="clipPage">Whether the product establishes an explicit page clip.</param>
    internal CorePdfRenderer(Extensibility.ICorePdfTextPainter? textPainter, bool clipPage)
    {
        this.textPainter = textPainter;
        this.clipPage = clipPage;
    }

    /// <summary>Gets the callback for image warnings, including decode failure details. Warnings are also written to trace listeners.</summary>
    public Action<RenderingDiagnostic>? DiagnosticHandler { get; init; }

    /// <summary>Gets or sets the diagnostic context for the page being appended.</summary>
    internal Action<RenderingDiagnostic>? PageDiagnosticHandler { get; set; }

    /// <summary>Saves the final shared PDF document exactly once.</summary>
    /// <param name="document">The session document.</param>
    /// <param name="output">The borrowed destination.</param>
    internal static void Save(PdfDocument document, Stream output)
    {
        document.Save(output, false);
        ConversionMetrics.Report("pdfSave", 1);
    }

    /// <summary>Appends exactly one page, including pages with no commands.</summary>
    /// <param name="document">The final document owned by the caller.</param>
    /// <param name="pageSettings">The output page dimensions.</param>
    /// <param name="commands">The commands on this page.</param>
    internal void AppendPage(PdfDocument document, PageSettings pageSettings, IEnumerable<DrawCommand> commands) =>
        AddPage(document, pageSettings, commands);

    /// <summary>Draws an image using the product placement policy.</summary>
    /// <param name="graphics">The page graphics.</param>
    /// <param name="command">The image command.</param>
    protected virtual void DrawImage(XGraphics graphics, DrawImageCommand command)
    {
        using var ownedResources = ImageResources.Current is null ? new ImageResources() : null;
        using var lease = ImageResources.Current!.Acquire<XImage>(command.ImageBytes, () => DecodePdfImage(command));
        var pdfImage = lease?.Value;
        if (pdfImage is null)
        {
            return;
        }

        graphics.DrawImage(pdfImage, ToRect(command.Bounds));
    }

    /// <summary>Decodes image bytes through the shared native-resource path.</summary>
    /// <param name="command">The source image command.</param>
    /// <returns>The decoded resource and its cache estimate.</returns>
    protected (XImage? Value, long Bytes) DecodePdfImage(DrawImageCommand command)
    {
        if (command.ImageBytes.Length == 0)
        {
            ReportImageFailure(command, "Image data is empty.");
            return (null, 0);
        }

        using var encoded = new SKMemoryStream(command.ImageBytes);
        using var codec = SKCodec.Create(encoded, out var codecResult);
        if (codec is null)
        {
            ReportImageFailure(command, $"SKCodec.Create returned null ({codecResult}); the image format may be unsupported or the data may be corrupt.");
            return (null, 0);
        }

        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
        {
            ReportImageFailure(command, $"SKBitmap.Decode returned null (format={codec.EncodedFormat}, size={codec.Info.Width}x{codec.Info.Height}); the image data could not be decoded.");
            return (null, 0);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = pngData.AsStream();
        var pdfImage = XImage.FromStream(stream);
        return (pdfImage, ((long)bitmap.RowBytes * bitmap.Height) + pngData.Size);
    }

    /// <summary>Completes extension processing for an appended page.</summary>
    /// <param name="page">The appended PDF page.</param>
    protected virtual void OnPageCompleted(PdfPage page)
    {
    }

    /// <summary>Attempts to draw an extension command in its original order.</summary>
    /// <param name="graphics">The page graphics.</param>
    /// <param name="command">The extension command.</param>
    /// <returns>Whether the command was handled.</returns>
    protected virtual bool TryDrawExtensionCommand(XGraphics graphics, DrawCommand command) => false;

    /// <summary>Executes one command, including nested extension-owned viewports.</summary>
    /// <param name="graphics">The page graphics.</param>
    /// <param name="command">The command to execute.</param>
    protected void Execute(XGraphics graphics, DrawCommand command)
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
                (textPainter ?? throw new InvalidOperationException("The conversion font is missing.")).Paint(graphics, text);
                break;
            case DrawLineCommand line:
                DrawStyledLine(graphics, line.Style, line.X1, line.Y1, line.X2, line.Y2);
                break;
            case DrawImageCommand image:
                DrawImage(graphics, image);
                break;
            default:
                if (!TryDrawExtensionCommand(graphics, command))
                {
                    throw new InvalidOperationException($"Unsupported internal drawing command: {command.GetType().FullName}.");
                }

                break;
        }
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

    private void ReportImageFailure(DrawImageCommand command, string reason)
    {
        var bounds = command.Bounds;
        var location = string.Format(CultureInfo.InvariantCulture, "image@{0},{1},{2},{3}", bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var header = BitConverter.ToString(command.ImageBytes, 0, Math.Min(16, command.ImageBytes.Length));
        var diagnostic = new RenderingDiagnostic(
            "ImageDecodeFailed",
            DiagnosticSeverity.Warning,
            DiagnosticStage.Render,
            $"画像を読み込めないためスキップしました。Skipped image: {reason} Bytes={command.ImageBytes.Length}; header={header}; bounds={location}; page={command.PageNumber}.",
            ObjectId: location,
            SourcePageNumber: command.PageNumber);
        Trace.TraceWarning("{0}: {1}", diagnostic.Code, diagnostic.Message);
        DiagnosticHandler?.Invoke(diagnostic);
        PageDiagnosticHandler?.Invoke(diagnostic);
    }

    private void AddPage(PdfDocument document, PageSettings pageSettings, IEnumerable<DrawCommand> commands)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageSettings.Width);
        page.Height = XUnit.FromPoint(pageSettings.Height);
        page.CropBox = page.MediaBox;
        using var graphics = XGraphics.FromPdfPage(page);
        if (clipPage)
        {
            graphics.IntersectClip(new XRect(0, 0, pageSettings.Width, pageSettings.Height));
        }

        foreach (var command in commands)
        {
            Execute(graphics, command);
        }

        OnPageCompleted(page);
    }
}
