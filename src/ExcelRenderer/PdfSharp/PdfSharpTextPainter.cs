using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using PdfSharp.Drawing;

namespace ExcelRenderer.PdfSharp;

/// <summary>Owns PDF text normalization and transforms and selects one concrete drawing path.</summary>
internal sealed class PdfSharpTextPainter
{
    private readonly PdfSharpFinalizedTextPainter _finalized = new();
    private readonly PdfSharpLegacyTextPainter _legacy;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpTextPainter"/> class.</summary>
    /// <param name="fontManager">The optional font manager used by the compatibility painter.</param>
    /// <param name="beforeResolvedDrawing">Optional internal compatibility drawing observer.</param>
    internal PdfSharpTextPainter(IFontManager? fontManager, Action? beforeResolvedDrawing = null) =>
        _legacy = new(fontManager, beforeResolvedDrawing);

    /// <summary>Paints text in PDF point coordinates and restores every transform it owns.</summary>
    /// <param name="graphics">The PDF graphics target, owned by the renderer.</param>
    /// <param name="command">The text command to paint.</param>
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
