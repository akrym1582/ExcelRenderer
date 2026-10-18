using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Pdf;

/// <summary>Finalizes compatibility and shape text before either PDF writer consumes a page.</summary>
internal static class PdfCommandFinalizer
{
    /// <summary>Resolves text through the shared, conversion-local layout service.</summary>
    /// <param name="commands">One page's commands.</param>
    /// <param name="layout">The shared layout service.</param>
    /// <param name="token">The conversion token.</param>
    /// <param name="resolvedRuns">The built-in font-run resolver for legacy layouts without stored runs.</param>
    /// <returns>Commands with resolved text, preserving existing finalized geometry.</returns>
    internal static IEnumerable<DrawCommand> Finalize(IEnumerable<DrawCommand> commands, ITextLayoutService layout, CancellationToken token, ITextLayoutService? resolvedRuns = null)
    {
        foreach (var command in commands)
        {
            token.ThrowIfCancellationRequested();
            yield return command switch
            {
                DrawTextCommand text => FinalizeText(text, layout, resolvedRuns ?? layout),
                DrawViewportCommand viewport => viewport with { Commands = Finalize(viewport.Commands, layout, token, resolvedRuns).ToArray() },
                DrawShapeCommand { Shape.Text: not null } shape => FinalizeShape(shape, layout, resolvedRuns ?? layout),
                _ => command,
            };
        }
    }

    private static DrawShapeCommand FinalizeShape(DrawShapeCommand shape, ITextLayoutService layout, ITextLayoutService resolvedRuns)
    {
        var text = shape.Shape.Text!;
        var bounds = new ReportRect(
            shape.Bounds.X + text.MarginLeft,
            shape.Bounds.Y + text.MarginTop,
            Math.Max(0, shape.Bounds.Width - text.MarginLeft - text.MarginRight),
            Math.Max(0, shape.Bounds.Height - text.MarginTop - text.MarginBottom));
        var style = CellStyle.Default with
        {
            Font = text.Font,
            HorizontalAlignment = text.HorizontalAlignment,
            VerticalAlignment = text.VerticalAlignment,
            WrapText = text.WrapText,
        };
        return shape with { TextCommand = FinalizeText(new(shape.PageNumber, bounds, text.Text, style), layout, resolvedRuns) };
    }

    private static DrawTextCommand FinalizeText(DrawTextCommand command, ITextLayoutService layout, ITextLayoutService resolvedRuns)
    {
        if (command.TextLayout is not null)
        {
            return CompleteRuns(command, resolvedRuns);
        }

        if (command.Style.TopToBottom || command.Style.TextRotation == 255)
        {
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(command.Text);
            var lines = new List<string>();
            while (elements.MoveNext())
            {
                lines.Add(elements.GetTextElement());
            }

            command = command with { Text = string.Join("\n", lines), Style = command.Style with { TextRotation = 0, TopToBottom = true } };
        }

        var measured = layout.Layout(command.Text, command.Style.Font, command.Bounds.Width, command.Style.WrapText);
        if (command.Style.ShrinkToFit && !command.Style.WrapText && command.Bounds.Width > 0 && measured.Size.Width > command.Bounds.Width)
        {
            measured = TextLayoutTransform.Scale(measured, command.Bounds.Width / measured.Size.Width);
        }

        return CompleteRuns(command with { TextLayout = measured }, resolvedRuns);
    }

    private static DrawTextCommand CompleteRuns(DrawTextCommand command, ITextLayoutService service)
    {
        var layout = command.TextLayout!;
        var size = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (size == 0 || !layout.Lines.Any(line => line.Text.Length > 0 && line.Runs.Count == 0))
        {
            return command;
        }

        // A caller-owned legacy measurer may supply line geometry without faces.
        // Resolve only the already-broken line's font runs. Preserve its width,
        // height, baseline and outer placement; never wrap or reflow it here.
        var lines = layout.Lines.Select(line => line.Text.Length == 0 || line.Runs.Count > 0
            ? line
            : line with { Runs = service.Layout(line.Text, command.Style.Font with { Size = size }, double.MaxValue, false).Lines.Single().Runs }).ToArray();
        if (lines.Any(line => line.Text.Length > 0 && line.Runs.Count == 0))
        {
            throw new InvalidOperationException("PDF text requires resolved font runs before drawing.");
        }

        return command with { TextLayout = layout with { Lines = lines } };
    }
}
