using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Drawing;
using ExcelRenderer.Slim.Fonts;
using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;
using PdfSharp.Drawing;

namespace ExcelRenderer.Slim.Pdf;

/// <summary>Draws finalized PDF lines at their stored point-coordinate baselines and run offsets.</summary>
internal sealed class PdfSharpFinalizedTextPainter
{
    private readonly SingleFontContext context;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpFinalizedTextPainter"/> class.</summary>
    /// <param name="context">The shared measurement and rendering font.</param>
    internal PdfSharpFinalizedTextPainter(SingleFontContext context) => this.context = context;

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
                var font = context.GetPdfFont(effectiveFontSize);
                graphics.DrawString(positioned.Line.Text, font, brush, new XPoint(positioned.Left, positioned.Baseline), XStringFormats.BaseLineLeft);

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

    private static XRect ToRect(ReportRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static XColor ToColor(ReportColor color) => XColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
}
