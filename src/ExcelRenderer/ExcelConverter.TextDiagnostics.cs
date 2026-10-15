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
        return Inspect();

        IEnumerable<DrawCommand> Inspect()
        {
            foreach (var (command, clipped) in Flatten(commands, false))
            {
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

                    if (options.DetectUnsupportedFeatures && page.Sheet.Cells.TryGetValue(address, out var source) && source.UnsupportedRichTextFormatting)
                    {
                        Add("UnsupportedRichTextFormatting", "Some partial text decorations or rotated formatting are not rendered.");
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
        }
    }

    private static void CollectUnsupportedFeatures(ReportSheet sheet, IReadOnlyList<Layout.PageSourceRegion>? regions, int? pageNumber, DiagnosticCollector diagnostics)
    {
        var geometry = new Layout.SheetGeometry(sheet);
        foreach (var feature in sheet.UnsupportedFeatures)
        {
            if (feature.Anchor is { } anchor)
            {
                var bounds = Layout.ObjectGeometry.GetSheetRect(geometry, anchor.From ?? new(1, 1), anchor.FromOffsetX, anchor.FromOffsetY, anchor.ExtentWidth, anchor.ExtentHeight, anchor);
                if (bounds.Width > 0 && bounds.Height > 0 && (regions is null || regions.Any(region => Layout.RectangleGeometry.Intersect(bounds, region.SourceBounds) is not null)))
                {
                    diagnostics.Add(new(feature.Code, DiagnosticSeverity.Warning, DiagnosticStage.Read, feature.Message, sheet.Name, feature.Ranges.Count == 0 ? null : string.Join(" ", feature.Ranges.Select(Markdown.MarkdownExporter.Range)), feature.Id, SourcePageNumber: pageNumber));
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
                diagnostics.Add(new(feature.Code, DiagnosticSeverity.Warning, DiagnosticStage.Read, feature.Message, sheet.Name, string.Join(" ", visible.Select(Markdown.MarkdownExporter.Range)), feature.Id, SourcePageNumber: pageNumber));
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
