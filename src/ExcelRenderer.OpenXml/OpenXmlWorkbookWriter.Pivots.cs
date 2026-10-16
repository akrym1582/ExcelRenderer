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
    private enum PivotCacheChange
    {
        Unchanged,
        SourceOnly,
        RecordsOnly,
        ItemsChanged,
    }

    private static Dictionary<Uri, PivotCacheUpdate> RewritePivotCaches(WorkbookPart workbook, ZipArchive archive, Dictionary<string, SheetSource> sources, Dictionary<string, SpillableBufferStream> replacements, StructuralReferences references, InputBufferOptions buffers, CancellationToken token)
    {
        var sheets = workbook.Workbook.Sheets!.Elements<S.Sheet>().ToDictionary(sheet => sheet.Name!.Value!, sheet => sources[workbook.GetPartById(sheet.Id!).Uri.ToString().TrimStart('/')], StringComparer.OrdinalIgnoreCase);
        var updates = new Dictionary<Uri, PivotCacheUpdate>();
        var indexedFields = new Dictionary<Uri, HashSet<int>>();
        foreach (var pivot in workbook.WorksheetParts.SelectMany(part => part.PivotTableParts))
        {
            var cacheUri = pivot.PivotTableCacheDefinitionPart!.Uri;
            if (!indexedFields.TryGetValue(cacheUri, out var indexed))
            {
                indexed = new HashSet<int>();
                indexedFields.Add(cacheUri, indexed);
            }

            var fields = pivot.PivotTableDefinition.PivotFields;
            if (fields != null)
            {
                foreach (var field in fields.Elements<S.PivotField>().Select((field, index) => (Field: field, Index: index)))
                {
                    if (field.Field.GetFirstChild<S.Items>() != null)
                    {
                        indexed.Add(field.Index);
                    }
                }
            }

            foreach (var data in pivot.PivotTableDefinition.DataFields?.Elements<S.DataField>() ?? Enumerable.Empty<S.DataField>())
            {
                if (data.BaseField?.Value is { } baseField && baseField >= 0)
                {
                    indexed.Add(baseField);
                }
            }
        }

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

            var update = new PivotCacheUpdate();
            updates.Add(cache.Uri, update);
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
                occurrences.Count != range.Last.Row - range.First.Row || occurrences.Where((row, index) => row.SourceRow != range.First.Row + index + 1).Any();
            if (!changed)
            {
                var updated = references.Rewrite(reference, name);
                if (updated != reference)
                {
                    update.Kind = PivotCacheChange.SourceOnly;
                    worksheet.SetAttributeValue("ref", updated);
                    Save(cache, definition);
                }

                continue;
            }

            var strings = ReadPivotStrings(workbook, source, occurrences, token);

            // Only unindexed measure dictionaries may be rebuilt. Every observable item ID
            // is retained, including missing values, so pivot item positions never change.
            var items = fields.Select((field, index) => indexedFields.TryGetValue(cache.Uri, out var indexed) && indexed.Contains(index) ?
                field.Element(Ns + "sharedItems")?.Elements().Select(value => new XElement(value)).ToList() ?? new List<XElement>() : new List<XElement>()).ToArray();
            var oldCounts = items.Select(values => values.Count).ToArray();
            var indices = items.Select(values =>
            {
                var result = new Dictionary<(string Type, string Value), int>();
                for (var index = 0; index < values.Count; index++)
                {
                    var key = PivotItemKey(values[index]);
                    if (!result.ContainsKey(key))
                    {
                        result.Add(key, index);
                    }
                }

                return result;
            }).ToArray();
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
                        var key = PivotItemKey(value);
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

            update.AddedItems = items.Select((values, index) => Enumerable.Range(oldCounts[index], values.Count - oldCounts[index]).ToArray()).ToArray();
            update.Kind = update.AddedItems.Any(values => values.Length > 0) ? PivotCacheChange.ItemsChanged : PivotCacheChange.RecordsOnly;
            worksheet.SetAttributeValue("ref", references.Rewrite(reference, name));
            definition.Root.SetAttributeValue("refreshOnLoad", 1);
            definition.Root.SetAttributeValue("invalid", 1);
            definition.Root.SetAttributeValue("recordCount", occurrences.Count);
            for (var index = 0; index < fields.Length; index++)
            {
                var values = items[index];
                var shared = new XElement(Ns + "sharedItems", new XAttribute("count", values.Count), values);
                var types = values.Select(value => value.Name.LocalName).Distinct().ToArray();
                shared.SetAttributeValue("longText", values.Any(value => value.Name == Ns + "s" && ((string?)value.Attribute("v"))?.Length > 255) ? 1 : 0);
                shared.SetAttributeValue("containsBlank", types.Contains("m") ? 1 : 0);
                shared.SetAttributeValue("containsString", types.Contains("s") ? 1 : 0);
                shared.SetAttributeValue("containsNumber", types.Contains("n") ? 1 : 0);
                shared.SetAttributeValue("containsDate", types.Contains("d") ? 1 : 0);
                shared.SetAttributeValue("containsNonDate", types.Any(type => type != "d") ? 1 : 0);
                shared.SetAttributeValue("containsInteger", types.Contains("n") && values.Where(value => value.Name == Ns + "n").All(value => double.TryParse((string?)value.Attribute("v"), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && Math.Truncate(number) == number) ? 1 : 0);
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

        return updates;

        void Save(OpenXmlPart part, XDocument xml)
        {
            var destination = new SpillableBufferStream(buffers);
            replacements.Add(part.Uri.ToString().TrimStart('/'), destination);
            xml.Save(destination);
        }
    }

    private static (string Type, string Value) PivotItemKey(XElement value)
    {
        var text = (string?)value.Attribute("v") ?? string.Empty;
        var type = value.Name.LocalName;
        if (type == "n" && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            text = number.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (type == "b")
        {
            text = text is "1" or "true" ? "1" : "0";
        }
        else if (type == "d" && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
        {
            text = date.ToString("O", CultureInfo.InvariantCulture);
        }

        return (type, text);
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

    private sealed class PivotCacheUpdate
    {
        internal PivotCacheChange Kind { get; set; }

        internal int[][] AddedItems { get; set; } = Array.Empty<int[]>();
    }
}
