using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer;

/// <summary>Collects opt-in finalized cell text diagnostics during selected-page preflight.</summary>
public static partial class ExcelConverter
{
    private static IEnumerable<DrawCommand> InspectCellText(SheetPage page, IEnumerable<DrawCommand> commands, DiagnosticOptions options, DiagnosticCollector diagnostics)
    {
        if (!options.EnableLayoutWarnings && !options.DetectUnsupportedFeatures)
        {
            return commands;
        }

        // Viewport preparation may enumerate commands repeatedly. Keep one diagnostic per
        // source cell on this output page while retaining the lazy command stream.
        var seen = new HashSet<(CellAddress Address, string Code)>();
        Layout.SheetGeometry? visualGeometry = null;
        return Inspect();

        IEnumerable<DrawCommand> Inspect()
        {
            Dictionary<string, ConversionDiagnostic>? clippedVisuals = null;
            foreach (var (command, clipped) in Flatten(commands, false))
            {
                if (options.DetectUnsupportedFeatures && command is DrawTextCommand { SourceAddress: { } visualAddress } &&
                    page.Sheet.Cells.TryGetValue(visualAddress, out var visualCell) && visualCell.ConditionalVisual?.Icon is { } icon &&
                    seen.Add((visualAddress, "ConditionalFormattingVisualClipped")))
                {
                    visualGeometry ??= new(page.Sheet);
                    var visualBounds = Layout.RectangleGeometry.Bounds(visualGeometry, new(visualAddress, new(visualAddress.Row + visualCell.RowSpan - 1, visualAddress.Column + visualCell.ColumnSpan - 1)));
                    if (!ConditionalCellVisualGeometry.CanDrawIcon(CoreIntegration.CorePageAdapter.ToCore(new Layout.RenderCell(visualCell, visualBounds))))
                    {
                        clippedVisuals ??= new(StringComparer.Ordinal);
                        var id = icon.RuleId ?? "conditional-format-icon";
                        clippedVisuals[id] = clippedVisuals.TryGetValue(id, out var previous) ? previous with { OccurrenceCount = previous.OccurrenceCount + 1 } :
                            new("ConditionalFormattingVisualClipped", DiagnosticSeverity.Warning, DiagnosticStage.Layout, "The icon has insufficient cell space; the saved value remains visible.", page.Sheet.Name, visualAddress.ToString(), ObjectId: id, SourcePageNumber: page.Descriptor.SourcePageNumber);
                    }
                }

                if (options.DetectUnsupportedFeatures && command is DrawTextCommand { SourceAddress: { } unsupportedAddress } &&
                    page.Sheet.Cells.TryGetValue(unsupportedAddress, out var unsupportedCell) && unsupportedCell.UnsupportedRichTextFormatting &&
                    seen.Add((unsupportedAddress, "UnsupportedRichTextFormatting")))
                {
                    diagnostics.Add(new("UnsupportedRichTextFormatting", DiagnosticSeverity.Warning, DiagnosticStage.Layout, "Some partial text decorations or rotated formatting are not rendered.", page.Sheet.Name, Markdown.MarkdownExporter.Range(new(unsupportedAddress, unsupportedAddress)), SourcePageNumber: page.Descriptor.SourcePageNumber));
                }

                if (options.DetectUnsupportedFeatures && command is DrawTextCommand { SourceAddress: { } fallbackAddress } &&
                    RenderResourceSession.Current?.TextMeasurer is not Abstractions.IRichTextLayoutService &&
                    page.Sheet.Cells.TryGetValue(fallbackAddress, out var fallbackCell) && fallbackCell.RichTextRuns is { Count: > 0 } &&
                    seen.Add((fallbackAddress, "RichTextLayoutFallback")))
                {
                    diagnostics.Add(new(
                        "RichTextLayoutFallback",
                        DiagnosticSeverity.Warning,
                        DiagnosticStage.Layout,
                        "The supplied measurement service has no partial-formatting capability; cell formatting is used for the complete text.",
                        page.Sheet.Name,
                        Markdown.MarkdownExporter.Range(new(fallbackAddress, fallbackAddress)),
                        SourcePageNumber: page.Descriptor.SourcePageNumber));
                }

                if (options.EnableLayoutWarnings && command is DrawTextCommand { SourceAddress: { } unmeasuredAddress, TextLayout: null } unmeasured &&
                    !string.IsNullOrWhiteSpace(unmeasured.Text) && unmeasured.Style.Font.Color?.Alpha != 0 &&
                    unmeasured.Style.Font.Size > 0 && unmeasured.Style.Font.Size < options.MinimumReadableFontSize &&
                    Layout.RectangleGeometry.Intersect(unmeasured.Bounds, new(0, 0, page.Descriptor.WidthPoints, page.Descriptor.HeightPoints)) is not null &&
                    seen.Add((unmeasuredAddress, "CellTextTooSmall")))
                {
                    diagnostics.Add(new("CellTextTooSmall", DiagnosticSeverity.Warning, DiagnosticStage.Layout, "Visible cell text is smaller than the configured minimum output font size.", page.Sheet.Name, Markdown.MarkdownExporter.Range(new(unmeasuredAddress, unmeasuredAddress)), SourcePageNumber: page.Descriptor.SourcePageNumber));
                }

                if (command is DrawTextCommand { SourceAddress: { } address, TextLayout: { } layout } text)
                {
                    if (options.EnableLayoutWarnings && !clipped && Core.Rendering.CellTextDiagnostics.IsClipped(
                        CoreIntegration.CoreTextLayoutAdapter.ToCore(layout), CoreIntegration.CoreModelAdapter.ToCore(text.Bounds), CoreIntegration.CoreModelAdapter.ToCore(text.Style)))
                    {
                        Add("CellTextClipped", "Finalized logical text extends beyond the cell content clip rectangle.");
                    }

                    var pageClip = new Layout.ReportRect(0, 0, page.Descriptor.WidthPoints, page.Descriptor.HeightPoints);
                    var clip = text.Style.WrapText || text.Style.ShrinkToFit
                        ? Layout.RectangleGeometry.Intersect(text.Bounds, pageClip) : pageClip;
                    var sizes = TextLayoutPlacement.Place(layout, text.Bounds, text.Style.HorizontalAlignment, text.Style.VerticalAlignment)
                        .Where(line => clip is { } visible && Layout.RectangleGeometry.Intersect(
                            new(line.Left, line.Baseline - line.Line.Ascent, line.Line.Width, line.Line.Ascent + line.Line.Descent), visible) is not null)
                        .SelectMany(line => line.Line.Runs.Where(run => clip is { } visible &&
                            Layout.RectangleGeometry.Intersect(new(line.Left + run.X, line.Baseline - line.Line.Ascent, run.Advance, line.Line.Ascent + line.Line.Descent), visible) is not null))
                        .Where(run => !string.IsNullOrWhiteSpace(run.Run.Text) && (run.Font ?? text.Style.Font).Color?.Alpha != 0)
                        .Select(run => run.Font?.Size ?? TextLayoutFontSize.Resolve(layout, text.Style.Font))
                        .Where(size => size > 0).ToArray();
                    if (options.EnableLayoutWarnings && sizes.Length > 0 && sizes.Min() < options.MinimumReadableFontSize)
                    {
                        Add("CellTextTooSmall", "A visible text run is smaller than the configured minimum output font size.");
                    }

                    void Add(string code, string message)
                    {
                        if (seen.Add((address, code)))
                        {
                            diagnostics.Add(new(code, DiagnosticSeverity.Warning, DiagnosticStage.Layout, message, page.Sheet.Name, Markdown.MarkdownExporter.Range(new(address, address)), SourcePageNumber: page.Descriptor.SourcePageNumber));
                        }
                    }
                }

                yield return command;
            }

            foreach (var diagnostic in clippedVisuals?.Values.AsEnumerable() ?? [])
            {
                diagnostics.Add(diagnostic);
            }
        }
    }

    private static void CollectCellReadDiagnostics(ReportSheet sheet, IReadOnlyList<Layout.PageSourceRegion>? regions, int? pageNumber, DiagnosticCollector diagnostics)
    {
        if (sheet.CellReadDiagnostics.Count == 0)
        {
            return;
        }

        var geometry = new Layout.SheetGeometry(sheet);
        foreach (var diagnostic in sheet.CellReadDiagnostics)
        {
            if (diagnostic.CellRange is null)
            {
                diagnostics.Add(diagnostic with { SourcePageNumber = pageNumber });
                continue;
            }

            var range = CellRangeParser.Parse(diagnostic.CellRange!, out _);
            if (range.First == range.Last && sheet.Cells.TryGetValue(range.First, out var sourceCell))
            {
                range = new(range.First, new(range.First.Row + sourceCell.RowSpan - 1, range.First.Column + sourceCell.ColumnSpan - 1));
            }

            if (sheet.RequestedRange is { } requested && (range.Last.Row < requested.First.Row || range.First.Row > requested.Last.Row || range.Last.Column < requested.First.Column || range.First.Column > requested.Last.Column))
            {
                continue;
            }

            var bounds = Layout.RectangleGeometry.Bounds(geometry, range);
            if (bounds.Width > 0 && bounds.Height > 0 && (regions is null || regions.Any(region => Layout.RectangleGeometry.Intersect(bounds, region.SourceBounds) is not null)))
            {
                diagnostics.Add(diagnostic with { SourcePageNumber = pageNumber });
            }
        }
    }

    private static void CollectUnsupportedFeatures(ReportSheet sheet, IReadOnlyList<Layout.PageSourceRegion>? regions, int? pageNumber, DiagnosticCollector diagnostics)
    {
        var geometry = new Layout.SheetGeometry(sheet);
        foreach (var feature in sheet.UnsupportedFeatures)
        {
            if (feature.IsSheetLevel)
            {
                diagnostics.Add(new(feature.Code, feature.Code == "ConditionalFormattingDisabled" ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, DiagnosticStage.Read, feature.Message, sheet.Name, ObjectId: feature.Id));
                continue;
            }

            if (feature.Anchor is { } anchor)
            {
                var bounds = Layout.ObjectGeometry.GetSheetRect(geometry, anchor.From ?? new(1, 1), anchor.FromOffsetX, anchor.FromOffsetY, anchor.ExtentWidth, anchor.ExtentHeight, anchor);
                if (bounds.Width > 0 && bounds.Height > 0 && (regions is null || regions.Any(region => Layout.RectangleGeometry.Intersect(bounds, region.SourceBounds) is not null)))
                {
                    diagnostics.Add(new(feature.Code, feature.Code == "ConditionalFormattingDisabled" ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, DiagnosticStage.Read, feature.Message, sheet.Name, feature.Ranges.Count == 0 ? null : string.Join(" ", feature.Ranges.Select(Markdown.MarkdownExporter.Range)), feature.Id, SourcePageNumber: pageNumber));
                }

                continue;
            }

            var visible = feature.Ranges.Where(range =>
            {
                var bounds = Layout.RectangleGeometry.Bounds(geometry, range);
                return bounds.Width > 0 && bounds.Height > 0 && (regions is null ||
                    regions.Any(region => Layout.RectangleGeometry.Intersect(bounds, region.SourceBounds) is not null));
            }).ToArray();
            if (visible.Length > 0)
            {
                diagnostics.Add(new(feature.Code, feature.Code == "ConditionalFormattingDisabled" ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, DiagnosticStage.Read, feature.Message, sheet.Name, string.Join(" ", visible.Select(Markdown.MarkdownExporter.Range)), feature.Id, SourcePageNumber: pageNumber));
            }
        }
    }

    private static IEnumerable<(DrawCommand Command, bool Clipped)> Flatten(IEnumerable<DrawCommand> commands, bool clipped)
    {
        foreach (var command in commands)
        {
            if (command is DrawViewportCommand viewport)
            {
                foreach (var item in Flatten(viewport.Commands, true))
                {
                    yield return item;
                }
            }
            else
            {
                yield return (command, clipped);
            }
        }
    }
}
