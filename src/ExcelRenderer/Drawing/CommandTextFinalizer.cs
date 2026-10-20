using ExcelRenderer.Abstractions;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>Finalizes compatibility and shape text before diagnostics, collection or drawing.</summary>
internal static class CommandTextFinalizer
{
    /// <summary>Finalizes raster/path compatibility input while preserving its native metrics.</summary>
    /// <param name="commands">The streaming command source.</param>
    /// <param name="fonts">The optional caller-selected font manager.</param>
    /// <param name="token">The conversion token.</param>
    /// <returns>Finalized commands, retaining streaming enumeration.</returns>
    internal static IEnumerable<DrawCommand> FinalizeRaster(IEnumerable<DrawCommand> commands, IFontManager? fonts, CancellationToken token)
    {
        var layout = Rendering.RenderResourceSession.Current?.RasterTextLayout ?? new SkiaSharp.SkiaCompatibilityTextLayout(fonts);
        return Finalize(commands, layout, token);
    }

    /// <summary>Reuses an unchanged page list and copies only when compatibility text needs completion.</summary>
    /// <param name="commands">The materialized current page.</param>
    /// <param name="layout">The command layout policy.</param>
    /// <param name="token">The conversion token.</param>
    /// <param name="resolvedRuns">The run completion policy.</param>
    /// <returns>The original page list, or one completed page list.</returns>
    internal static IReadOnlyList<DrawCommand> FinalizePage(IReadOnlyList<DrawCommand> commands, ITextLayoutService layout, CancellationToken token, ITextLayoutService? resolvedRuns = null)
    {
        DrawCommand[]? changed = null;
        var index = 0;
        foreach (var finalized in Finalize(commands, layout, token, resolvedRuns))
        {
            if (!ReferenceEquals(finalized, commands[index]))
            {
                changed ??= commands.ToArray();
                changed[index] = finalized;
            }

            index++;
        }

        return changed ?? commands;
    }

    /// <summary>Resolves text through the shared, conversion-local layout service.</summary>
    /// <param name="commands">One page's commands.</param>
    /// <param name="layout">The shared layout service.</param>
    /// <param name="token">The conversion token.</param>
    /// <param name="resolvedRuns">The built-in font-run resolver for legacy layouts without stored runs.</param>
    /// <returns>Commands with resolved text, preserving existing finalized geometry.</returns>
    internal static IEnumerable<DrawCommand> Finalize(IEnumerable<DrawCommand> commands, ITextLayoutService layout, CancellationToken token, ITextLayoutService? resolvedRuns = null)
    {
        using var resources = ConversionFontResources.Current is null ? new ConversionFontResources() : null;
        foreach (var command in commands)
        {
            token.ThrowIfCancellationRequested();
            var start = Rendering.ConversionMetrics.Observer is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
            var finalized = FinalizeCommand(command);
            if (start != 0)
            {
                Rendering.ConversionMetrics.Report("textFinalize.ms", (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            }

            yield return finalized;
        }

        DrawCommand FinalizeCommand(DrawCommand command)
        {
            token.ThrowIfCancellationRequested();
            if (command is DrawViewportCommand viewport)
            {
                DrawCommand[]? changed = null;
                for (var index = 0; index < viewport.Commands.Count; index++)
                {
                    var child = viewport.Commands[index];
                    var finalized = FinalizeCommand(child);
                    if (!ReferenceEquals(child, finalized))
                    {
                        changed ??= viewport.Commands.ToArray();
                        changed[index] = finalized;
                    }
                }

                return changed is null ? viewport : viewport with { Commands = changed };
            }

            return command switch
            {
                DrawTextCommand text => FinalizeText(text, layout, resolvedRuns ?? layout),
                DrawShapeCommand { Shape.Text: not null } shape => FinalizeShape(shape, layout, resolvedRuns ?? layout),
                _ => command,
            };
        }
    }

    private static DrawShapeCommand FinalizeShape(DrawShapeCommand shape, ITextLayoutService layout, ITextLayoutService resolvedRuns)
    {
        if (shape.TextCommand is { } existing)
        {
            var finalized = FinalizeText(existing, layout, resolvedRuns);
            return ReferenceEquals(existing, finalized) ? shape : shape with { TextCommand = finalized };
        }

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

            command = command with { OriginalText = command.OriginalText ?? command.Text, Text = string.Join("\n", lines), Style = command.Style with { TextRotation = 0, TopToBottom = true } };
        }

        Rendering.ConversionMetrics.Report("unfinalizedTextLayouts", 1);
        var measured = layout is ICommandTextLayoutService compatibility ? compatibility.Layout(command) :
            layout.Layout(command.Text, command.Style.Font, command.Bounds.Width, command.Style.WrapText);
        if (layout is not ICommandTextLayoutService && command.Style.ShrinkToFit && !command.Style.WrapText && command.Bounds.Width > 0 && measured.Size.Width > command.Bounds.Width)
        {
            measured = TextLayoutTransform.Scale(measured, command.Bounds.Width / measured.Size.Width);
        }

        return CompleteRuns(command with { TextLayout = measured }, resolvedRuns);
    }

    private static bool NeedsCompletion(TextLayoutResult layout)
    {
        for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
        {
            var line = layout.Lines[lineIndex];
            var runs = line.Runs;
            if (line.Text.Length > 0 && runs.Count == 0)
            {
                return true;
            }

            for (var runIndex = 0; runIndex < runs.Count; runIndex++)
            {
                if (runs[runIndex].Glyphs is null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static DrawTextCommand CompleteRuns(DrawTextCommand command, ITextLayoutService service)
    {
        var layout = command.TextLayout!;
        var size = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (size == 0 || !NeedsCompletion(layout))
        {
            return command;
        }

        // A caller-owned legacy measurer may supply line geometry without faces.
        // Resolve only the already-broken line's font runs. Preserve its width,
        // height, baseline and outer placement; never wrap or reflow it here.
        TextLayoutLine[]? lines = null;
        for (var index = 0; index < layout.Lines.Count; index++)
        {
            var line = layout.Lines[index];
            var runs = line.Runs;
            if (line.Text.Length > 0 && runs.Count == 0)
            {
                Rendering.ConversionMetrics.Report("fontRunCompletions", 1);
                runs = service.Layout(line.Text, command.Style.Font with { Size = size }, double.MaxValue, false).Lines.Single().Runs;
                if (runs.Count == 0)
                {
                    throw new InvalidOperationException("Text requires resolved font runs before drawing.");
                }
            }

            TextLayoutRun[]? completed = null;
            for (var runIndex = 0; runIndex < runs.Count; runIndex++)
            {
                var run = runs[runIndex];
                if (run.Glyphs is null)
                {
                    completed ??= runs.ToArray();
                    completed[runIndex] = ResolvedGlyphLayout.Finalize(run);
                }
            }

            if (!ReferenceEquals(runs, line.Runs) || completed is not null)
            {
                lines ??= layout.Lines.ToArray();
                lines[index] = line with { Runs = completed is null ? runs : Array.AsReadOnly(completed) };
            }
        }

        return lines is null ? command : command with { TextLayout = layout with { Lines = lines } };
    }
}
