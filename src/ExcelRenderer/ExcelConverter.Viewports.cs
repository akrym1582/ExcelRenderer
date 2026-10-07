using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;

namespace ExcelRenderer;

/// <summary>Prepares shared final output geometry and hyperlink metadata.</summary>
public static partial class ExcelConverter
{
    private static IReadOnlyList<SelectedSheet> ApplyRanges(IReadOnlyList<SelectedSheet> sheets, SelectionOptions selection, DiagnosticCollector diagnostics)
    {
        if (selection.Ranges is null)
        {
            return sheets;
        }

        var ranges = new Dictionary<string, CellRange>(StringComparer.Ordinal);
        long count = 0;
        foreach (var item in selection.Ranges)
        {
            if (item is null || string.IsNullOrEmpty(item.SheetName) || !sheets.Any(s => s.Sheet.Name == item.SheetName) || ranges.ContainsKey(item.SheetName))
            {
                throw new ArgumentException("Each explicit range must refer to a different selected worksheet.");
            }

            count = checked(count + CellRangeParser.CountCells(item.Range));
            if (count > selection.MaxRangeCells)
            {
                throw new ArgumentOutOfRangeException(nameof(selection), "Explicit ranges exceed MaxRangeCells.");
            }

            ranges.Add(item.SheetName, item.Range);
        }

        return sheets.Select(selected =>
        {
            if (!ranges.TryGetValue(selected.Sheet.Name, out var range))
            {
                return selected;
            }

            var sheet = selected.Sheet;
            foreach (var merged in sheet.MergedRanges.Where(m => m.First.Row <= range.Last.Row && m.Last.Row >= range.First.Row &&
                         m.First.Column <= range.Last.Column && m.Last.Column >= range.First.Column &&
                         (!range.Contains(m.First) || !range.Contains(m.Last))))
            {
                diagnostics.Add(new(
                    "ClippedMergedCell",
                    DiagnosticSeverity.Warning,
                    DiagnosticStage.Layout,
                    "The explicit selection clips an original merged cell.",
                    sheet.Name,
                    Markdown.MarkdownExporter.Range(merged)));
            }

            IndexRange? Limit(IndexRange? title, int first, int last) => title is { } t && Math.Max(t.First, first) <= Math.Min(t.Last, last)
                ? new(Math.Max(t.First, first), Math.Min(t.Last, last)) : null;
            return selected with
            {
                Sheet = sheet with
                {
                    RequestedRange = range,
                    PrintArea = range,
                    PrintAreas = [],
                    PageSettings = sheet.PageSettings with
                    {
                        TitleRows = Limit(sheet.PageSettings.TitleRows, range.First.Row, range.Last.Row),
                        TitleColumns = Limit(sheet.PageSettings.TitleColumns, range.First.Column, range.Last.Column),
                    },
                },
            };
        }).ToArray();
    }

    private static SheetPage PrepareViewport(SheetPage page, IEnumerable<DrawCommand> commands, RenderRequest request, FontManager fonts, DiagnosticCollector diagnostics)
    {
        var original = new ReportRect(0, 0, page.Descriptor.WidthPoints, page.Descriptor.HeightPoints);
        var content = request.Trim.Enabled || page.Sheet.RequestedRange is not null || (commands is IReadOnlyCollection<DrawCommand> collection ? collection.Count == 0 : !commands.Any())
            ? DrawCommandBounds.Get(commands, original, fonts) : original;
        if (content is null)
        {
            diagnostics.Add(new(
                "EmptyContent",
                DiagnosticSeverity.Info,
                DiagnosticStage.Layout,
                "No visible drawing content remains on this page.",
                page.Sheet.Name));
        }

        var viewport = new PageViewport(
            original.Width,
            original.Height,
            request.Trim.Enabled ? content ?? new(0, 0, 1, 1) : original,
            request.Trim.Enabled ? request.Trim.PaddingPoints : 0);
        if (!double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) || viewport.Width <= 0 || viewport.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Output dimensions must be finite and positive.");
        }

        (int? Width, int? Height) pixels = request.OutputFormat == OutputFormat.Png
            ? PngRenderer.GetPixelDimensions(viewport.Width, viewport.Height, request.Dpi, request.MaxPngPixels)
            : GetContinuousPixelDimensions(viewport.Width, viewport.Height, request.Dpi);
        return page with
        {
            Viewport = viewport,
            Descriptor = page.Descriptor with
            {
                WidthPoints = viewport.Width,
                HeightPoints = viewport.Height,
                PixelWidth = pixels.Width,
                PixelHeight = pixels.Height,
                OriginalWidthPoints = request.Trim.Enabled ? original.Width : null,
                OriginalHeightPoints = request.Trim.Enabled ? original.Height : null,
                CropBounds = request.Trim.Enabled ? viewport.Crop : null,
                PaddingPoints = request.Trim.Enabled ? viewport.Padding : null,
            },
        };
    }

    private static IReadOnlyList<SheetPage> ResolvePdfLinks(IReadOnlyList<SheetPage> pages, WorkbookRenderMetadata metadata, DiagnosticCollector diagnostics)
    {
        return pages.Select(page =>
        {
            var links = new List<ResolvedPdfHyperlink>();
            foreach (var link in page.Sheet.Hyperlinks)
            {
                var geometry = metadata.Geometry(page.Sheet.Name);
                var merged = page.Sheet.MergedRanges.FirstOrDefault(m => m.Contains(link.SourceRange.First) && link.SourceRange.First == link.SourceRange.Last);
                var source = RectangleGeometry.Bounds(geometry, merged == default ? link.SourceRange : merged);
                var rectangles = (page.Regions ?? []).Select(region => RectangleGeometry.Intersect(source, region.SourceBounds) is { } clipped
                    ? RectangleGeometry.Intersect(region.Map(clipped), region.PageBounds) : null)
                    .Where(rect => rect is not null).Select(rect => RectangleGeometry.Intersect(rect!.Value, page.Viewport!.Crop))
                    .Where(rect => rect is not null).Select(rect => page.Viewport!.Map(rect!.Value)).Distinct().ToArray();
                if (rectangles.Length == 0 && link.SourceRange != default)
                {
                    continue;
                }

                var code = link.IssueCode;
                var reason = link.IssueReason;
                string? uri = null;
                SheetPage? targetPage = null;
                ReportRect? targetPoint = null;
                if (code is null && link.IsExternal)
                {
                    uri = HyperlinkPolicy.External(link.Target, link.Location);
                    if (uri is null)
                    {
                        code = "HyperlinkRejected";
                        reason = "The external hyperlink violates the supported URI policy.";
                    }
                }
                else if (code is null)
                {
                    if (HyperlinkPolicy.Internal(page.Sheet, link.Target, metadata.Sheets, out var targetSheet, out var address, out var failureCode))
                    {
                        var targetGeometry = metadata.Geometry(targetSheet!.Name);
                        var targetMerged = targetSheet!.MergedRanges.FirstOrDefault(m => m.Contains(address));
                        var targetBounds = RectangleGeometry.Bounds(targetGeometry, targetMerged == default ? new(address, address) : targetMerged);
                        var candidates = pages.Where(p => p.Sheet.Name == targetSheet.Name).SelectMany(p => (p.Regions ?? []).Select(region =>
                        {
                            var intersection = RectangleGeometry.Intersect(targetBounds, region.SourceBounds);
                            var mapped = intersection is { } fragment ? RectangleGeometry.Intersect(region.Map(fragment), p.Viewport!.Crop) : null;
                            if (mapped is not null && targetMerged == default)
                            {
                                var point = region.Map(targetBounds);
                                var crop = p.Viewport!.Crop;
                                if (point.X < crop.X || point.Y < crop.Y || point.X >= crop.X + crop.Width || point.Y >= crop.Y + crop.Height)
                                {
                                    mapped = null;
                                }
                                else
                                {
                                    mapped = point;
                                }
                            }

                            return (Page: p, Region: region, Bounds: mapped);
                        })).Where(c => c.Bounds is not null).OrderBy(c => c.Region.IsTitle).ThenBy(c => c.Page.Descriptor.OutputPageNumber).ToArray();
                        if (candidates.Length > 0)
                        {
                            targetPage = candidates[0].Page;
                            targetPoint = targetPage.Viewport!.Map(candidates[0].Bounds!.Value);
                        }
                    }

                    if (targetPage is null)
                    {
                        code = failureCode;
                        reason = "The internal hyperlink target is unsupported or not visible in the selected output.";
                    }
                }

                if (code is not null)
                {
                    diagnostics.Add(new(
                        code,
                        DiagnosticSeverity.Warning,
                        DiagnosticStage.Layout,
                        reason!,
                        page.Sheet.Name,
                        link.SourceRange == default ? null : Markdown.MarkdownExporter.Range(link.SourceRange)));
                    continue;
                }

                foreach (var rect in rectangles)
                {
                    links.Add(new(
                        rect,
                        uri,
                        targetPage?.Descriptor.OutputPageNumber,
                        targetPoint?.X,
                        targetPoint?.Y,
                        targetPage?.Descriptor.HeightPoints,
                        link.Tooltip));
                }
            }

            return page with { Links = links };
        }).ToArray();
    }
}
