using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Input;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml;

/// <summary>Rebuilds saved worksheet pivot source records without calculating pivot aggregates.</summary>
public static partial class OpenXmlWorkbookWriter
{
    private static void RewritePivotCaches(WorkbookPart workbook, ZipArchive archive, Dictionary<string, SheetSource> sources, Dictionary<string, SpillableBufferStream> replacements, StructuralReferences references, InputBufferOptions buffers, CancellationToken token)
    {
        var sheets = workbook.Workbook.Sheets!.Elements<S.Sheet>().ToDictionary(sheet => sheet.Name!.Value!, sheet => sources[workbook.GetPartById(sheet.Id!).Uri.ToString().TrimStart('/')], StringComparer.OrdinalIgnoreCase);
        foreach (var cache in workbook.Parts.Select(pair => pair.OpenXmlPart).OfType<PivotTableCacheDefinitionPart>())
        {
            token.ThrowIfCancellationRequested();
            using var input = archive.GetEntry(cache.Uri.ToString().TrimStart('/'))!.Open();
            var definition = XDocument.Load(input);
            var worksheet = definition.Root!.Element(Ns + "cacheSource")?.Element(Ns + "worksheetSource") ?? throw new NotSupportedException("A pivot cache requires a worksheet source.");
            var name = (string?)worksheet.Attribute("sheet");
            var reference = (string?)worksheet.Attribute("ref");
            if (name == null || reference == null || !sheets.TryGetValue(name, out var source))
            {
                throw new NotSupportedException("A pivot cache requires an explicit local worksheet rectangle.");
            }

            var range = CellRange.Parse(reference);
            RequireUniqueRows(source.Plan, range.First.Row, range.First.Row, "A pivot header intersects repeated or deleted rows.");
            var placements = StructuralReferences.Expand(reference, source.Plan);
            if (placements.Count != 1)
            {
                throw new NotSupportedException("A pivot source must remain a single rectangle.");
            }

            var fields = definition.Root.Element(Ns + "cacheFields")?.Elements().ToArray() ?? throw new InvalidDataException("Missing pivot cache fields.");
            if (fields.Length != range.Last.Column - range.First.Column + 1)
            {
                throw new NotSupportedException("Grouped pivot source fields are unsupported.");
            }

            var occurrences = source.Plan.Rows.Where(row => row.SourceRow > range.First.Row && row.SourceRow <= range.Last.Row).ToList();
            for (var row = Math.Max(source.Plan.SourceLastRow + 1, range.First.Row + 1); row <= range.Last.Row; row++)
            {
                occurrences.Add(new OutputRowOccurrence(row, references.FirstRow(source.Plan, row)!.Value, 0, 0, Array.Empty<CellValueEdit>()));
            }

            var changed = occurrences.Any(row => row.Values.Any(edit => edit.Column >= range.First.Column && edit.Column <= range.Last.Column)) ||
                occurrences.Count != range.Last.Row - range.First.Row || occurrences.Any(row => row.OccurrenceId != 0);
            if (!changed)
            {
                var updated = references.Rewrite(reference, name);
                if (updated != reference)
                {
                    worksheet.SetAttributeValue("ref", updated);
                    Save(cache, definition);
                }

                continue;
            }

            var strings = ReadPivotStrings(workbook, source, occurrences, token);
            var items = fields.Select(_ => new List<XElement>()).ToArray();
            var indices = fields.Select(_ => new Dictionary<string, int>(StringComparer.Ordinal)).ToArray();
            var recordsPart = cache.PivotTableCacheRecordsPart ?? throw new NotSupportedException("A worksheet pivot cache requires an existing records part.");
            var recordBuffer = new SpillableBufferStream(buffers);
            replacements.Add(recordsPart.Uri.ToString().TrimStart('/'), recordBuffer);
            using (var recordWriter = System.Xml.XmlWriter.Create(recordBuffer, new System.Xml.XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), CloseOutput = false }))
            {
                recordWriter.WriteStartElement("pivotCacheRecords", Ns.NamespaceName);
                recordWriter.WriteAttributeString("count", occurrences.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var occurrence in occurrences)
                {
                    token.ThrowIfCancellationRequested();
                    var row = source.ReadRow(occurrence.SourceRow);
                    var cells = new Dictionary<int, XElement>();
                    var sourceColumn = 0;
                    foreach (var cell in row.Elements(Ns + "c"))
                    {
                        sourceColumn = cell.Attribute("r") is { } address ? CellAddress.Parse(address.Value).Column : sourceColumn + 1;
                        cells.Add(sourceColumn, cell);
                    }

                    var record = new XElement(Ns + "r");
                    for (var index = 0; index < fields.Length; index++)
                    {
                        var column = range.First.Column + index;
                        var edit = occurrence.Values.FirstOrDefault(value => value.Column == column);
                        var cell = cells.GetValueOrDefault(column);
                        var value = edit == null ? PivotCell(cell, strings) : PivotValue(edit.Value, workbook.Workbook.WorkbookProperties?.Date1904?.Value == true);
                        var key = value.ToString(SaveOptions.DisableFormatting);
                        if (!indices[index].TryGetValue(key, out var item))
                        {
                            item = items[index].Count;
                            items[index].Add(value);
                            indices[index].Add(key, item);
                        }

                        record.Add(new XElement(Ns + "x", new XAttribute("v", item)));
                    }

                    record.WriteTo(recordWriter);
                }

                recordWriter.WriteEndElement();
            }

            worksheet.SetAttributeValue("ref", references.Rewrite(reference, name));
            definition.Root.SetAttributeValue("refreshOnLoad", 1);
            definition.Root.SetAttributeValue("invalid", 1);
            definition.Root.SetAttributeValue("recordCount", occurrences.Count);
            for (var index = 0; index < fields.Length; index++)
            {
                var values = items[index];
                var shared = new XElement(Ns + "sharedItems", new XAttribute("count", values.Count), values);
                var types = values.Select(value => value.Name.LocalName).Distinct().ToArray();
                shared.SetAttributeValue("containsBlank", types.Contains("m") ? 1 : 0);
                shared.SetAttributeValue("containsString", types.Contains("s") ? 1 : 0);
                shared.SetAttributeValue("containsNumber", types.Contains("n") ? 1 : 0);
                shared.SetAttributeValue("containsMixedTypes", types.Length > 1 ? 1 : 0);
                shared.SetAttributeValue("containsSemiMixedTypes", types.Any(type => type is "s" or "m") ? 1 : 0);
                if (fields[index].Element(Ns + "sharedItems") is { } old)
                {
                    old.ReplaceWith(shared);
                }
                else
                {
                    fields[index].AddFirst(shared);
                }
            }

            Save(cache, definition);
        }

        void Save(OpenXmlPart part, XDocument xml)
        {
            var destination = new SpillableBufferStream(buffers);
            replacements.Add(part.Uri.ToString().TrimStart('/'), destination);
            xml.Save(destination);
        }
    }

    private static IReadOnlyDictionary<int, string> ReadPivotStrings(WorkbookPart workbook, SheetSource source, IEnumerable<OutputRowOccurrence> occurrences, CancellationToken token)
    {
        var required = new HashSet<int>();
        foreach (var row in occurrences.Select(item => item.SourceRow).Distinct())
        {
            token.ThrowIfCancellationRequested();
            foreach (var cell in source.ReadRow(row).Elements(Ns + "c").Where(cell => (string?)cell.Attribute("t") == "s"))
            {
                required.Add(int.Parse(cell.Element(Ns + "v")!.Value, CultureInfo.InvariantCulture));
            }
        }

        var result = new Dictionary<int, string>();
        if (required.Count == 0)
        {
            return result;
        }

        using var input = workbook.SharedStringTablePart?.GetStream() ?? throw new InvalidDataException("Missing shared string table.");
        using var reader = System.Xml.XmlReader.Create(input, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        var index = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.NodeType != System.Xml.XmlNodeType.Element || reader.LocalName != "si")
            {
                continue;
            }

            if (required.Remove(index))
            {
                using var item = reader.ReadSubtree();
                var element = XElement.Load(item);
                result.Add(index, string.Concat(element.Descendants(Ns + "t").Where(text => !text.Ancestors(Ns + "rPh").Any()).Select(text => text.Value)));
                if (required.Count == 0)
                {
                    return result;
                }
            }

            index++;
        }

        throw new InvalidDataException("Missing pivot source shared string.");
    }

    private static XElement PivotCell(XElement? cell, IReadOnlyDictionary<int, string> strings)
    {
        var value = cell?.Element(Ns + "v")?.Value;
        var type = (string?)cell?.Attribute("t");
        if (type == "inlineStr")
        {
            return new XElement(Ns + "s", new XAttribute("v", string.Concat(cell!.Descendants(Ns + "t").Select(text => text.Value))));
        }

        if (value == null)
        {
            return new XElement(Ns + "m");
        }

        return new XElement(Ns + (type == "s" ? "s" : type == "b" ? "b" : type == "e" ? "e" : type == "d" ? "d" : "n"), new XAttribute("v", type == "s" ? strings[int.Parse(value, CultureInfo.InvariantCulture)] : value));
    }

    private static XElement PivotValue(object? value, bool date1904)
    {
        if (value == null)
        {
            return new XElement(Ns + "m");
        }

        if (value is string text)
        {
            return new XElement(Ns + "s", new XAttribute("v", EncodeText(text.StartsWith("'", StringComparison.Ordinal) ? text.Substring(1) : text)));
        }

        if (value is bool boolean)
        {
            return new XElement(Ns + "b", new XAttribute("v", boolean ? 1 : 0));
        }

        var number = value is DateTime date ? SerialDate(date, date1904) : value is TimeSpan time ? time.TotalDays : (double)value;
        return new XElement(Ns + "n", new XAttribute("v", number.ToString("R", CultureInfo.InvariantCulture)));
    }
}
