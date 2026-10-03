using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Owns Skia text transforms and selects the finalized or compatibility path.</summary>
internal sealed class SkiaTextPainter
{
    private readonly SkiaFinalizedTextPainter _finalized;
    private readonly SkiaLegacyTextPainter _legacy;

    /// <summary>Initializes a new instance of the <see cref="SkiaTextPainter"/> class.Provides the backend-specific pagination or text operation.</summary>
    /// <param name="drawFinalized">The drawFinalized value.</param>
    /// <param name="drawLegacy">The drawLegacy value.</param>
    internal SkiaTextPainter(
        Action<SKCanvas, DrawTextCommand, TextLayoutResult> drawFinalized,
        Action<SKCanvas, DrawTextCommand> drawLegacy)
    {
        _finalized = new(drawFinalized);
        _legacy = new(drawLegacy);
    }

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <param name="canvas">The canvas value.</param>
    /// <param name="command">The command value.</param>
    internal void Paint(SKCanvas canvas, DrawTextCommand command)
    {
        command = NormalizeVerticalText(command);
        var rotation = command.Style.TextRotation == 255 ? 0 : command.Style.TextRotation;
        if (rotation == 0)
        {
            PaintCore(canvas, command);
            return;
        }

        canvas.Save();
        try
        {
            canvas.ClipRect(ToRect(command.Bounds));
            canvas.RotateDegrees(
                (float)rotation,
                (float)(command.Bounds.X + (command.Bounds.Width / 2)),
                (float)(command.Bounds.Y + (command.Bounds.Height / 2)));
            PaintCore(canvas, command);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static SKRect ToRect(ReportRect rect) => new(
        (float)rect.X,
        (float)rect.Y,
        (float)(rect.X + rect.Width),
        (float)(rect.Y + rect.Height));

    private static DrawTextCommand NormalizeVerticalText(DrawTextCommand command)
    {
        if (command.TextLayout is not null || (!command.Style.TopToBottom && command.Style.TextRotation != 255))
        {
            return command;
        }

        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(command.Text);
        var result = new List<string>();
        while (elements.MoveNext())
        {
            result.Add(elements.GetTextElement());
        }

        return command with
        {
            Text = string.Join("\n", result),
            Style = command.Style with { TextRotation = 0, TopToBottom = true },
        };
    }

    private void PaintCore(SKCanvas canvas, DrawTextCommand command)
    {
        if (command.TextLayout is { } layout)
        {
            _finalized.Paint(canvas, command, layout);
        }
        else
        {
            _legacy.Paint(canvas, command);
        }
    }
}
