using System.Diagnostics;
using System.Globalization;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;
using ExcelRenderer.OpenXml.Reader.Input;
using SkiaSharp;
using RenderingDiagnostic = ExcelRenderer.Core.Rendering.ConversionDiagnostic;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Owns Core's basic Skia PDF rendering using a single validated font snapshot.</summary>
internal sealed class CorePdfRenderer : IDisposable
{
    private readonly PdfFontCollection fonts;
    private readonly SkiaPdfDocument document;
    private readonly Extensibility.ICorePdfTextPainter? textPainter;
    private readonly CancellationToken token;

    /// <summary>Initializes a new instance of the <see cref="CorePdfRenderer"/> class.</summary>
    /// <param name="context">The conversion-owned single font, or null for image-only output.</param>
    /// <param name="token">The conversion token.</param>
    internal CorePdfRenderer(SingleFontContext? context = null, CancellationToken token = default)
    {
        this.token = token;
        fonts = new(token);
        document = new(new InputBufferOptions { MemoryThresholdBytes = 8 * 1024 * 1024, AllowTemporaryFiles = true }, token);
        textPainter = context is null ? null : new SkiaFinalizedTextPainter(context, fonts);
    }

    /// <summary>Gets the diagnostic callback.</summary>
    internal Action<RenderingDiagnostic>? DiagnosticHandler { get; init; }

    /// <summary>Gets or sets the current page diagnostic callback.</summary>
    internal Action<RenderingDiagnostic>? PageDiagnosticHandler { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            document.Dispose();
        }
        finally
        {
            fonts.Dispose();
        }
    }

    /// <summary>Creates the shared image decode warning without owning backend resources.</summary>
    /// <param name="command">The failed image command.</param>
    /// <param name="reason">The backend decode failure details.</param>
    /// <returns>The warning with stable page and image location metadata.</returns>
    internal static RenderingDiagnostic CreateImageFailureDiagnostic(DrawImageCommand command, string reason)
    {
        var bounds = command.Bounds;
        var location = string.Format(CultureInfo.InvariantCulture, "image@{0},{1},{2},{3}", bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var header = BitConverter.ToString(command.ImageBytes, 0, Math.Min(16, command.ImageBytes.Length));
        return new RenderingDiagnostic(
            "ImageDecodeFailed",
            DiagnosticSeverity.Warning,
            DiagnosticStage.Render,
            $"画像を読み込めないためスキップしました。Skipped image: {reason} Bytes={command.ImageBytes.Length}; header={header}; bounds={location}; page={command.PageNumber}.",
            ObjectId: location,
            SourcePageNumber: command.PageNumber);
    }

    /// <summary>Collects font usage before document creation.</summary>
    /// <param name="commands">One page's finalized commands.</param>
    internal void Collect(IEnumerable<DrawCommand> commands)
    {
        foreach (var text in commands.OfType<DrawTextCommand>())
        {
            token.ThrowIfCancellationRequested();
            (textPainter ?? throw new InvalidOperationException("The conversion font is missing.")).Collect(text);
        }
    }

    /// <summary>Subsets collected fonts and starts the shared document.</summary>
    internal void Prepare()
    {
        fonts.Prepare();
        document.Prepare();
    }

    /// <summary>Appends one page without changing finalized geometry.</summary>
    /// <param name="settings">The exact dimensions.</param>
    /// <param name="commands">One page's commands.</param>
    internal void AppendPage(PageSettings settings, IEnumerable<DrawCommand> commands) =>
        document.Append(new(settings.Width, settings.Height, []), canvas =>
        {
            foreach (var command in commands)
            {
                token.ThrowIfCancellationRequested();
                Execute(canvas, command);
            }
        });

    /// <summary>Saves exactly once without closing the caller's stream.</summary>
    /// <param name="output">The borrowed destination.</param>
    internal void Complete(Stream output) => document.Complete(output);

    private void Execute(SKCanvas canvas, DrawCommand command)
    {
        if (command.ClipBounds is not { } clip)
        {
            ExecuteUnclipped(canvas, command);
            return;
        }

        canvas.Save();
        try
        {
            canvas.ClipRect(new((float)clip.X, (float)clip.Y, (float)(clip.X + clip.Width), (float)(clip.Y + clip.Height)));
            ExecuteUnclipped(canvas, command);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void ExecuteUnclipped(SKCanvas canvas, DrawCommand command)
    {
        switch (command)
        {
            case FillRectangleCommand fill:
                using (var paint = SkiaDrawingPrimitives.CreatePaint(fill.Color, SKPaintStyle.Fill))
                {
                    var b = fill.Bounds;
                    canvas.DrawRect((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height, paint);
                }

                break;
            case DrawBorderCommand border:
                SkiaDrawingPrimitives.DrawBorder(canvas, border);
                break;
            case DrawLineCommand line:
                SkiaDrawingPrimitives.DrawStyledLine(canvas, line.Style, line.X1, line.Y1, line.X2, line.Y2);
                break;
            case DrawTextCommand text:
                (textPainter ?? throw new InvalidOperationException("The conversion font is missing.")).Paint(canvas, text);
                break;
            case DrawImageCommand image:
                DrawImage(canvas, image);
                break;
            default:
                throw new InvalidOperationException($"Unsupported internal drawing command: {command.GetType().FullName}.");
        }
    }

    private void DrawImage(SKCanvas canvas, DrawImageCommand command)
    {
        using var ownedResources = ImageResources.Current is null ? new ImageResources() : null;
        using var lease = ImageResources.Current!.AcquireImage(command.ImageBytes, out var failure);
        token.ThrowIfCancellationRequested();
        if (lease?.Value.PdfImage is not { } decoded)
        {
            var diagnostic = CreateImageFailureDiagnostic(command, failure!);
            Trace.TraceWarning("{0}: {1}", diagnostic.Code, diagnostic.Message);
            DiagnosticHandler?.Invoke(diagnostic);
            PageDiagnosticHandler?.Invoke(diagnostic);
            return;
        }

        var b = command.Bounds;
        SkiaDrawingPrimitives.DrawPdfImage(canvas, decoded, new(0, 0, decoded.Width, decoded.Height), new((float)b.X, (float)b.Y, (float)(b.X + b.Width), (float)(b.Y + b.Height)));
    }
}
