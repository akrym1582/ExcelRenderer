using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Excel;

/// <summary>Reads original hyperlink XML and constant formulas before ClosedXML display access.</summary>
internal static class HyperlinkReader
{
    /// <summary>Reads definitions, scoped names without deriving display values from formulas.</summary>
    /// <param name="stream">The seekable XLSX stream.</param>
    /// <param name="selectedSheets">Optional selected bodies; unselected sheets retain link metadata without cell text.</param>
    /// <returns>Metadata by actual sheet name.</returns>
    internal static IReadOnlyDictionary<string, SheetHyperlinkMetadata> Read(Stream stream, IReadOnlyList<string>? selectedSheets = null)
    {
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart!;
        var sheets = workbook.Workbook.Sheets!.Elements<Sheet>().ToArray();
        var names = workbook.Workbook.DefinedNames?.Elements<DefinedName>().ToArray() ?? [];
        var result = new Dictionary<string, SheetHyperlinkMetadata>(StringComparer.Ordinal);
        for (var sheetIndex = 0; sheetIndex < sheets.Length; sheetIndex++)
        {
            var sheet = sheets[sheetIndex];
            if (workbook.GetPartById(sheet.Id!) is not WorksheetPart part)
            {
                continue;
            }

            var includeBody = selectedSheets is null || selectedSheets.Contains(sheet.Name!.Value!, StringComparer.Ordinal);
            var links = new List<ReportHyperlink>();
            var relationships = part.HyperlinkRelationships.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var definition = 0;
            foreach (var raw in ReadElements<Hyperlink>(part))
            {
                CellRange range;
                try
                {
                    range = CellRangeParser.Parse(raw.Reference?.Value ?? string.Empty, out var qualifier);
                    if (qualifier is not null)
                    {
                        throw new ArgumentException();
                    }
                }
                catch (ArgumentException)
                {
                    links.Add(new(default, string.Empty, false, DefinitionId: "xml-" + ++definition)
                    {
                        IssueCode = "HyperlinkRejected", IssueReason = "Invalid hyperlink source reference.",
                    });
                    continue;
                }

                var id = raw.Id?.Value;
                var target = raw.Location?.Value ?? string.Empty;
                string? issue = null;
                var external = id is not null;
                if (id is not null)
                {
                    if (relationships.TryGetValue(id, out var relationship) && relationship.IsExternal)
                    {
                        target = relationship.Uri.OriginalString;
                    }
                    else
                    {
                        issue = "Missing or invalid hyperlink relationship.";
                    }
                }

                if (links.Any(link => Overlaps(link.SourceRange, range)))
                {
                    issue = "Overlapping hyperlink definition; the first XML definition takes precedence.";
                }

                links.Add(new(range, target, external, external ? raw.Location?.Value : null, raw.Tooltip?.Value, "xml-" + ++definition)
                {
                    IssueCode = issue is null ? null : "HyperlinkRejected", IssueReason = issue,
                });
            }

            foreach (var cell in (includeBody ? ReadElements<Cell>(part) : Enumerable.Empty<Cell>()).Where(c => c.CellFormula is not null))
            {
                var formula = cell.CellFormula!.Text;
                if (formula.IndexOf("HYPERLINK", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var range = CellRangeParser.Parse(cell.CellReference!.Value!, out _);
                var supported = LiteralHyperlinkFormula.TryRead(formula, out var target, out _);
                if (links.Any(link => link.SourceRange.Contains(range.First)))
                {
                    continue;
                }

                links.Add(new(range, target, !target.StartsWith("#", StringComparison.Ordinal), DefinitionId: "formula-" + cell.CellReference)
                {
                    IssueCode = supported ? null : "HyperlinkUnsupported",
                    IssueReason = supported ? null : "Only literal-string HYPERLINK arguments are supported.",
                });
            }

            var scoped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names.Where(n => n.LocalSheetId is null))
            {
                scoped[name.Name!.Value!] = name.Text;
            }

            foreach (var name in names.Where(n => n.LocalSheetId?.Value == sheetIndex))
            {
                scoped[name.Name!.Value!] = name.Text;
            }

            result[sheet.Name!.Value!] = new(links, scoped)
            {
                OriginalCells = (includeBody ? ReadElements<Cell>(part) : Enumerable.Empty<Cell>()).Where(cell => cell.CellReference is not null)
                    .Select(cell => CellRangeParser.Parse(cell.CellReference!.Value!, out _).First).ToHashSet(),
            };
        }

        return result;
    }

    private static IEnumerable<T> ReadElements<T>(WorksheetPart part)
        where T : OpenXmlElement
    {
        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            if (reader.IsStartElement && reader.ElementType == typeof(T))
            {
                yield return (T)reader.LoadCurrentElement()!;
            }
        }
    }

    private static bool Overlaps(CellRange a, CellRange b) =>
        a.First.Row <= b.Last.Row && a.Last.Row >= b.First.Row && a.First.Column <= b.Last.Column && a.Last.Column >= b.First.Column;
}
