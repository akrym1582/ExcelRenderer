using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using ExcelRenderer.OpenXml.Diagnostics;
using ExcelRenderer.OpenXml.Input;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Writing.Plans;

namespace ExcelRenderer.OpenXml.Writing;

/// <summary>Transforms geometry using explicit one-to-many row occurrences.</summary>
public static partial class OpenXmlWorkbookWriter
{
    private static List<string> RewriteMerges(SheetSource source)
    {
        var result = new List<string>();
        foreach (var element in source.Children.Where(child => child.Name.LocalName == "mergeCells").SelectMany(child => child.Elements()))
        {
            var range = CellRange.Parse((string)element.Attribute("ref")!);
            foreach (var first in source.Occurrences.GetValueOrDefault(range.First.Row, new List<OutputRowOccurrence>()))
            {
                if (!source.Endpoints.TryGetValue((range.Last.Row, first.OccurrenceId), out var last))
                {
                    throw new NotSupportedException("Merged range cannot be transformed within a single repeat occurrence.");
                }

                result.Add(new CellAddress(first.OutputRow, range.First.Column) + ":" + new CellAddress(last, range.Last.Column));
            }
        }

        return result;
    }

    private static int[] RewriteRowBreaks(SheetSource source)
    {
        var result = new HashSet<int>(source.Plan.RowBreaks);
        foreach (var original in source.Children.Where(child => child.Name.LocalName == "rowBreaks").SelectMany(child => child.Elements()).Select(child => (int)child.Attribute("id")!))
        {
            if (original > source.Plan.SourceLastRow)
            {
                var shifted = checked(original + source.Plan.Rows.Count - source.Plan.SourceLastRow);
                if (shifted is > 0 and <= 1048576)
                {
                    result.Add(shifted);
                }
            }
            else
            {
                foreach (var occurrence in source.Occurrences.GetValueOrDefault(original, new List<OutputRowOccurrence>()))
                {
                    result.Add(occurrence.OutputRow);
                }
            }
        }

        return result.Where(value => value > 0).OrderBy(value => value).ToArray();
    }

    private static void WriteBreaks(XmlWriter writer, string name, IReadOnlyCollection<int> breaks, int maximum)
    {
        if (breaks.Count > 0)
        {
            new XElement(
                Ns + name,
                new XAttribute("count", breaks.Count),
                new XAttribute("manualBreakCount", breaks.Count),
                breaks.Select(value => new XElement(Ns + "brk", new XAttribute("id", value), new XAttribute("min", 0), new XAttribute("max", maximum), new XAttribute("man", 1)))).WriteTo(writer);
        }
    }

    private sealed class SheetSource : IDisposable
    {
        private readonly SpillableBufferStream rows;
        private readonly RowPrototypeCache prototypes = new RowPrototypeCache();
        private readonly Dictionary<int, (long Offset, int Length)> locations = new Dictionary<int, (long Offset, int Length)>();

        internal SheetSource(Stream input, SheetEditPlan plan, InputBufferOptions buffers, CancellationToken token)
        {
            Plan = plan;
            rows = new SpillableBufferStream(buffers);
            try
            {
                using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                reader.MoveToContent();
                Root = new XElement(Ns + "worksheet");
                while (reader.MoveToNextAttribute())
                {
                    var name = reader.Name == "xmlns" ? XNamespace.None + "xmlns" : reader.Prefix == "xmlns" ? XNamespace.Xmlns + reader.LocalName : XName.Get(reader.LocalName, reader.NamespaceURI);
                    Root.Add(new XAttribute(name, reader.Value));
                }

                reader.MoveToElement();
                reader.ReadStartElement();
                var lastRow = 0;
                while (reader.MoveToContent() == XmlNodeType.Element)
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.LocalName != "sheetData")
                    {
                        Children.Add((XElement)XNode.ReadFrom(reader));
                        continue;
                    }

                    Children.Add(new XElement(Ns + "sheetData"));
                    if (reader.IsEmptyElement)
                    {
                        reader.Read();
                        continue;
                    }

                    reader.ReadStartElement();
                    while (reader.MoveToContent() == XmlNodeType.Element)
                    {
                        token.ThrowIfCancellationRequested();
                        var row = (XElement)XNode.ReadFrom(reader);
                        var sourceRow = (int?)row.Attribute("r") ?? lastRow + 1;
                        lastRow = sourceRow;
                        var column = 0;
                        foreach (var cell in row.Elements(Ns + "c"))
                        {
                            column = cell.Attribute("r") is { } reference ? CellAddress.Parse(reference.Value.Replace("$", string.Empty)).Column : column + 1;
                            LastColumn = Math.Max(LastColumn, column);
                        }

                        var bytes = Encoding.UTF8.GetBytes(row.ToString(SaveOptions.DisableFormatting));
                        locations.Add(sourceRow, (rows.Position, bytes.Length));
                        rows.Write(bytes, 0, bytes.Length);
                    }

                    reader.ReadEndElement();
                }

                LastColumn = Math.Max(LastColumn, plan.Rows.SelectMany(row => row.Values).Select(edit => edit.Column).DefaultIfEmpty(0).Max());
                OperationMetrics.Report("writer.rowStoreBytes", rows.Length);
                foreach (var occurrence in plan.Rows)
                {
                    if (!Occurrences.TryGetValue(occurrence.SourceRow, out var list))
                    {
                        list = new List<OutputRowOccurrence>();
                        Occurrences.Add(occurrence.SourceRow, list);
                    }

                    list.Add(occurrence);
                    Endpoints.Add((occurrence.SourceRow, occurrence.OccurrenceId), occurrence.OutputRow);
                }
            }
            catch
            {
                rows.Dispose();
                throw;
            }
        }

        internal XElement Root { get; }

        internal SheetEditPlan Plan { get; }

        internal List<XElement> Children { get; } = new List<XElement>();

        internal Dictionary<int, List<OutputRowOccurrence>> Occurrences { get; } = new Dictionary<int, List<OutputRowOccurrence>>();

        internal Dictionary<(int Row, int Occurrence), int> Endpoints { get; } = new Dictionary<(int Row, int Occurrence), int>();

        internal int LastColumn { get; }

        public void Dispose()
        {
            rows.Dispose();
            OperationMetrics.Report("writer.rowCacheHits", prototypes.Hits);
            OperationMetrics.Report("writer.rowCacheEntries", prototypes.Count);
            OperationMetrics.Report("writer.rowCacheSourceBytes", prototypes.SourceBytes);
        }

        internal IEnumerable<OutputRowOccurrence> OutputRows()
        {
            foreach (var occurrence in Plan.Rows)
            {
                yield return occurrence;
            }

            var delta = Plan.Rows.Count - Plan.SourceLastRow;
            foreach (var sourceRow in locations.Keys.Where(row => row > Plan.SourceLastRow).OrderBy(row => row))
            {
                var outputRow = checked(sourceRow + delta);
                if (outputRow is < 1 or > 1048576)
                {
                    throw new NotSupportedException("Trailing row metadata would move outside the Excel grid.");
                }

                yield return new OutputRowOccurrence(sourceRow, outputRow, 0, 0, Array.Empty<CellValueEdit>());
            }
        }

        internal XElement ReadRow(int row)
        {
            if (!locations.TryGetValue(row, out var location))
            {
                return new XElement(Ns + "row");
            }

            var cacheable = location.Length <= RowPrototypeCache.MaximumRowBytes && Occurrences.TryGetValue(row, out var occurrences) && occurrences.Count > 1;
            if (cacheable && prototypes.Clone(row) is { } cached)
            {
                return cached;
            }

            rows.Position = location.Offset;
            var bytes = new byte[location.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = rows.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }

            var parsed = XElement.Parse(Encoding.UTF8.GetString(bytes));
            return cacheable ? prototypes.Store(row, bytes.Length, parsed) : parsed;
        }

        internal int ColumnStyle(int column) => Children.Where(child => child.Name.LocalName == "cols").SelectMany(child => child.Elements()).LastOrDefault(child => (int)child.Attribute("min")! <= column && (int)child.Attribute("max")! >= column)?.Attribute("style") is { } style ? int.Parse(style.Value, CultureInfo.InvariantCulture) : 0;
    }
}
