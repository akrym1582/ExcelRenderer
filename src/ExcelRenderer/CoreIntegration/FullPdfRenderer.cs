using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using PdfSharp.Drawing;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Adds transformed images, shapes, and viewports to the shared PDF page renderer.</summary>
internal sealed class FullPdfRenderer : Core.Pdf.CorePdfRenderer
{
    private readonly PdfSharpTextPainter textPainter;

    /// <summary>Initializes a new instance of the <see cref="FullPdfRenderer"/> class.</summary>
    /// <param name="fontManager">The session font manager.</param>
    internal FullPdfRenderer(IFontManager? fontManager)
        : base(new FullTextPainter(fontManager), false)
    {
        textPainter = new(fontManager);
    }

    /// <summary>Gets the product callback for final-page annotation preparation.</summary>
    internal Action<global::PdfSharp.Pdf.PdfPage>? PageCompleted { get; init; }

    /// <inheritdoc/>
    protected override void OnPageCompleted(global::PdfSharp.Pdf.PdfPage page) => PageCompleted?.Invoke(page);

    /// <inheritdoc/>
    protected override void DrawImage(XGraphics graphics, Core.Drawing.DrawImageCommand command)
    {
        var full = (command.ExtensionData as CoreCommandAdapter.CommandData)?.Command as DrawImageCommand
            ?? throw new InvalidOperationException("Full image metadata is missing.");
        using var ownedResources = ImageResources.Current is null ? new ImageResources() : null;
        using var lease = ImageResources.Current!.Acquire<XImage>(command.ImageBytes, () => DecodePdfImage(command));
        var image = lease?.Value;
        if (image is null)
        {
            return;
        }

        var state = graphics.Save();
        try
        {
            if (full.ClipBounds is { } clip)
            {
                graphics.IntersectClip(ToRect(clip));
            }

            var centerX = full.Bounds.X + (full.Bounds.Width / 2);
            var centerY = full.Bounds.Y + (full.Bounds.Height / 2);
            graphics.TranslateTransform(centerX, centerY);
            graphics.RotateTransform(full.Rotation);
            graphics.ScaleTransform(full.FlipHorizontal ? -1 : 1, full.FlipVertical ? -1 : 1);
            graphics.TranslateTransform(-centerX, -centerY);
            if (full.Crop is { } crop)
            {
                var source = new XRect(
                    crop.Left * image.PointWidth,
                    crop.Top * image.PointHeight,
                    Math.Max(0, 1 - crop.Left - crop.Right) * image.PointWidth,
                    Math.Max(0, 1 - crop.Top - crop.Bottom) * image.PointHeight);
                graphics.DrawImage(image, ToRect(full.Bounds), source, XGraphicsUnit.Point);
            }
            else
            {
                graphics.DrawImage(image, ToRect(full.Bounds));
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <inheritdoc/>
    protected override bool TryDrawExtensionCommand(XGraphics graphics, Core.Drawing.DrawCommand command)
    {
        if (command.ExtensionData is not CoreCommandAdapter.CommandData data)
        {
            return false;
        }

        switch (data.Command)
        {
            case DrawViewportCommand viewport:
                var state = graphics.Save();
                try
                {
                    graphics.TranslateTransform(viewport.OffsetX, viewport.OffsetY);
                    graphics.IntersectClip(ToRect(viewport.Clip));
                    foreach (var child in viewport.Commands)
                    {
                        Execute(graphics, CoreCommandAdapter.ToCore(child));
                    }
                }
                finally
                {
                    graphics.Restore(state);
                }

                break;
            case DrawShapeCommand shape:
                DrawShape(graphics, shape);
                break;
        }

        // The public renderer historically ignores user-defined commands it does not support.
        return true;
    }

    private static XRect ToRect(ReportRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static XColor ToColor(ReportColor color) => XColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    private void DrawShape(XGraphics graphics, DrawShapeCommand command)
    {
        var state = graphics.Save();
        try
        {
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
                textPainter.Paint(
                    graphics,
                    new DrawTextCommand(
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
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private sealed class FullTextPainter(IFontManager? fontManager) : Core.Extensibility.ICorePdfTextPainter
    {
        private readonly PdfSharpTextPainter painter = new(fontManager);

        public void Paint(XGraphics graphics, Core.Drawing.DrawTextCommand command)
        {
            var full = (command.ExtensionData as CoreCommandAdapter.CommandData)?.Command as DrawTextCommand
                ?? throw new InvalidOperationException("Full text metadata is missing.");
            painter.Paint(graphics, full);
        }
    }
}
