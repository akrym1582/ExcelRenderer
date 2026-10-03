using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using PdfSharp.Drawing;

namespace ExcelRenderer.PdfSharp;

/// <summary>Owns PDF text transforms and selects the finalized or compatibility path.</summary>
internal sealed class PdfSharpTextPainter
{
    private readonly PdfSharpFinalizedTextPainter _finalized;
    private readonly PdfSharpLegacyTextPainter _legacy;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpTextPainter"/> class.Provides the backend-specific pagination or text operation.</summary>
    /// <param name="drawFinalized">The drawFinalized value.</param>
    /// <param name="drawLegacy">The drawLegacy value.</param>
    internal PdfSharpTextPainter(
        Action<XGraphics, DrawTextCommand, TextLayoutResult> drawFinalized,
        Action<XGraphics, DrawTextCommand> drawLegacy)
    {
        _finalized = new(drawFinalized);
        _legacy = new(drawLegacy);
    }

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <param name="graphics">The graphics value.</param>
    /// <param name="command">The command value.</param>
    internal void Paint(XGraphics graphics, DrawTextCommand command)
    {
        command = NormalizeVerticalText(command);
        var rotation = command.Style.TextRotation == 255 ? 0 : command.Style.TextRotation;
        if (rotation == 0)
        {
            PaintCore(graphics, command);
            return;
        }

        var state = graphics.Save();
        try
        {
            graphics.IntersectClip(ToRect(command.Bounds));
            graphics.RotateAtTransform(rotation, new(
                command.Bounds.X + (command.Bounds.Width / 2),
                command.Bounds.Y + (command.Bounds.Height / 2)));
            PaintCore(graphics, command);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static XRect ToRect(ReportRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

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

    private void PaintCore(XGraphics graphics, DrawTextCommand command)
    {
        if (command.TextLayout is { } layout)
        {
            _finalized.Paint(graphics, command, layout);
        }
        else
        {
            _legacy.Paint(graphics, command);
        }
    }
}
