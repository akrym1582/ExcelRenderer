using System.Text;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Markdown;

/// <summary>Resolves hyperlinks once for every Markdown display path.</summary>
internal sealed class MarkdownHyperlinks
{
    private readonly Dictionary<string, List<(ReportHyperlink Link, string Uri)>> links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<CellAddress>> anchors = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<ReportSheet> sheets;

    /// <summary>Initializes a new instance of the <see cref="MarkdownHyperlinks"/> class.</summary>
    /// <param name="document">The selected document with original sheet identities.</param>
    /// <param name="mode">The preservation mode.</param>
    /// <param name="diagnostics">The optional request collector.</param>
    internal MarkdownHyperlinks(ReportDocument document, HyperlinkMode mode, DiagnosticCollector? diagnostics)
    {
        if (!Enum.IsDefined(typeof(HyperlinkMode), mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        sheets = document.Sheets;
        if (mode == HyperlinkMode.None)
        {
            return;
        }

        foreach (var sheet in sheets)
        {
            var resolved = new List<(ReportHyperlink Link, string Uri)>();
            links[sheet.Name] = resolved;
            foreach (var link in sheet.Hyperlinks)
            {
                if (link.SourceRange != default && !Visible(sheet, link.SourceRange))
                {
                    continue;
                }

                var code = link.IssueCode;
                var reason = link.IssueReason;
                string? uri = null;
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
                    if (HyperlinkPolicy.Internal(sheet, link.Target, sheets, out var targetSheet, out var address, out var failureCode) &&
                        Visible(targetSheet!, new(address, address)))
                    {
                        uri = "#" + Anchor(targetSheet!, address);
                        if (!anchors.TryGetValue(targetSheet!.Name, out var addresses))
                        {
                            addresses = [];
                            anchors[targetSheet.Name] = addresses;
                        }

                        addresses.Add(address);
                    }
                    else
                    {
                        code = failureCode;
                        reason = "The internal hyperlink target is unsupported or not visible in this Markdown document.";
                    }
                }

                if (code is not null)
                {
                    diagnostics?.Add(new(
                        code,
                        DiagnosticSeverity.Warning,
                        DiagnosticStage.Layout,
                        reason!,
                        sheet.Name,
                        link.SourceRange == default ? null : MarkdownExporter.Range(link.SourceRange)));
                }
                else if (uri is not null)
                {
                    resolved.Add((link, uri));
                }
            }
        }
    }

    /// <summary>Encodes display labels separately from URI and attribute markup.</summary>
    /// <param name="label">Plain display text.</param>
    /// <param name="uri">The normalized target, or null.</param>
    /// <param name="html">Whether this is an HTML cell.</param>
    /// <param name="table">Whether this is a Markdown table cell.</param>
    /// <param name="tooltip">An optional plain tooltip.</param>
    /// <returns>Ready-to-write markup.</returns>
    internal static string Format(string label, string? uri, bool html, bool table, string? tooltip = null)
    {
        var text = html ? Html(label) : Escape(label, table);
        if (uri is null || string.IsNullOrWhiteSpace(label))
        {
            return text;
        }

        var tip = tooltip is null ? null : new string(tooltip.Where(c => !char.IsControl(c)).Take(512).ToArray());
        var title = tip is null ? string.Empty : html ? " title=\"" + Html(tip) + "\"" :
            " \"" + tip.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("|", "&#124;").Replace("<", "&lt;").Replace(">", "&gt;") + "\"";
        return html ? "<a href=\"" + Html(uri) + "\"" + title + ">" + text + "</a>" :
            "[" + text + "](<" + uri.Replace("<", "%3C").Replace(">", "%3E").Replace("|", "%7C").Replace("\\", "%5C") + ">" + title + ")";
    }

    /// <summary>Attaches single-cell links to their display cells.</summary>
    /// <param name="sheet">The current sheet.</param>
    /// <param name="cells">The visible cells.</param>
    /// <returns>Cells with separate resolved targets.</returns>
    internal IReadOnlyList<VisualCell> Attach(ReportSheet sheet, IReadOnlyList<VisualCell> cells) => cells.Select(cell => cell with
    {
        LinkTooltip = links.GetValueOrDefault(sheet.Name)?.FirstOrDefault(pair => pair.Link.SourceRange.First == cell.Range.First).Link?.Tooltip,
        AnchorId = anchors.GetValueOrDefault(sheet.Name)?.Contains(cell.Range.First) == true ? Anchor(sheet, cell.Range.First) : null,
        Hyperlink = links.GetValueOrDefault(sheet.Name)?.FirstOrDefault(pair =>
            pair.Link.SourceRange.First == pair.Link.SourceRange.Last && pair.Link.SourceRange.First == cell.Range.First).Uri,
    }).ToArray();

    /// <summary>Writes explicit named targets, including empty target cells.</summary>
    /// <param name="output">The Markdown builder.</param>
    /// <param name="sheet">The target sheet.</param>
    /// <param name="represented">Addresses receiving an anchor in an output region.</param>
    internal void WriteTargets(StringBuilder output, ReportSheet sheet, IReadOnlyCollection<CellAddress> represented)
    {
        if (!anchors.TryGetValue(sheet.Name, out var addresses))
        {
            return;
        }

        foreach (var address in addresses.Where(address => !represented.Contains(address)).OrderBy(a => a.Row).ThenBy(a => a.Column))
        {
            output.Append("<a id=\"").Append(Anchor(sheet, address)).AppendLine("\"></a>");
            output.Append("- Link target: ").AppendLine(MarkdownExporter.Range(new(address, address)));
        }

        output.AppendLine();
    }

    /// <summary>Writes links which have no natural nonempty single display cell.</summary>
    /// <param name="output">The Markdown builder.</param>
    /// <param name="sheet">The source sheet.</param>
    /// <param name="cells">The displayed cells.</param>
    internal void WriteList(StringBuilder output, ReportSheet sheet, IReadOnlyList<VisualCell> cells)
    {
        var list = links.GetValueOrDefault(sheet.Name)?.Where(pair => pair.Link.SourceRange.First != pair.Link.SourceRange.Last ||
            !cells.Any(cell => cell.Range.First == pair.Link.SourceRange.First && !string.IsNullOrWhiteSpace(cell.Text))).ToArray() ?? [];
        if (list.Length == 0)
        {
            return;
        }

        output.AppendLine("### Cell links").AppendLine();
        foreach (var pair in list)
        {
            var label = "Link at " + MarkdownExporter.Range(pair.Link.SourceRange);
            output.Append("- ").AppendLine(Format(label, pair.Uri, html: false, table: false, tooltip: pair.Link.Tooltip));
        }

        output.AppendLine();
    }

    private static string Html(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;").Replace("\r", string.Empty).Replace("\n", "<br>");

    private static string Escape(string value, bool table)
    {
        var result = Html(value).Replace("\\", "\\\\").Replace("`", "\\`").Replace("*", "\\*").Replace("_", "\\_")
            .Replace("[", "\\[").Replace("]", "\\]").Replace("(", "\\(").Replace(")", "\\)");
        return table ? result.Replace("|", "\\|") : result;
    }

    private static bool Visible(ReportSheet sheet, CellRange range) =>
        RectangleGeometry.Bounds(new SheetGeometry(sheet), range) is { Width: > 0, Height: > 0 };

    private string Anchor(ReportSheet sheet, CellAddress address) =>
        $"xl-s{(sheet.SourceSheetIndex > 0 ? sheet.SourceSheetIndex : sheets.ToList().IndexOf(sheet) + 1)}-r{address.Row}-c{address.Column}";
}
