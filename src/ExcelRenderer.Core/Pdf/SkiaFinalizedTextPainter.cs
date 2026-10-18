using System.Text;
using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using SkiaSharp;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Draws Core's finalized single-font lines without repeating layout.</summary>
internal sealed class SkiaFinalizedTextPainter(SingleFontContext context, PdfFontCollection fonts) : Extensibility.ICorePdfTextPainter
{
    /// <inheritdoc/>
    public void Paint(SKCanvas canvas, DrawTextCommand command)
    {
        var layout = command.TextLayout ?? throw new InvalidOperationException("Text layout is missing.");
        var size = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (size == 0)
        {
            return;
        }

        canvas.Save();
        try
        {
            if (command.Style.WrapText || command.Style.ShrinkToFit)
            {
                var b = command.Bounds;
                canvas.ClipRect(new((float)b.X, (float)b.Y, (float)(b.X + b.Width), (float)(b.Y + b.Height)));
            }

            using var paint = SkiaDrawingPrimitives.CreatePaint(command.Style.Font.Color ?? new(0, 0, 0), SKPaintStyle.Fill);
            foreach (var position in TextLayoutPlacement.Place(layout, command.Bounds, command.Style.HorizontalAlignment, command.Style.VerticalAlignment))
            {
                var text = position.Line.Text;
                var selection = position.Line.Glyphs ?? new(context.Face, context.Face.Typeface.GetGlyphs(text), text, text);
                fonts.Draw(canvas, context.Face, selection.Glyphs.ToArray(), selection.Source.ToArray(), selection.Clusters.ToArray(), (float)size, (float)position.Left, (float)position.Baseline, paint);
                if (command.Style.Font.Underline && !fonts.IsCollecting)
                {
                    canvas.DrawLine((float)position.Left, (float)(position.Baseline + 1), (float)(position.Left + position.Line.Width), (float)(position.Baseline + 1), paint);
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }
}
