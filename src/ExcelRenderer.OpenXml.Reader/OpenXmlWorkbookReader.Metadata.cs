using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using ExcelRenderer.OpenXml.Reader.Infrastructure;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using ExcelRenderer.OpenXml.Reader.References;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml.Reader;

/// <summary>Streams worksheet cells and sparse metadata.</summary>
public sealed partial class OpenXmlWorkbookReader
{
    private static XElement AttributesElement(OpenXmlReader reader) => new XElement(XmlValues.Spreadsheet + reader.LocalName, reader.Attributes.Select(a => new XAttribute(a.LocalName, a.Value)));

    private static int NextRow(XElement e, int previous)
    {
        var row = XmlValues.Int(e, "r") ?? checked(previous + 1);
        if (row <= previous || row > 1048576)
        {
            throw new InvalidDataException("Worksheet rows must increase within the XLSX grid.");
        }

        return row;
    }

    private static CellAddress NextAddress(string? reference, int row, int previousColumn)
    {
        if (row < 1 || previousColumn >= 16384)
        {
            throw new InvalidDataException("Invalid cell position.");
        }

        var address = reference == null ? new CellAddress(row, checked(previousColumn + 1)) : CellAddress.Parse(reference);
        if (address.Row != row || address.Column <= previousColumn)
        {
            throw new InvalidDataException("Cell coordinates must agree with their row and increase strictly.");
        }

        return address;
    }

    private static List<ColumnInfo> OverlayColumn(List<ColumnInfo> existing, ColumnInfo value)
    {
        var result = new List<ColumnInfo>();
        foreach (var c in existing)
        {
            if (c.Max < value.Min || c.Min > value.Max)
            {
                result.Add(c);
                continue;
            }

            if (c.Min < value.Min)
            {
                result.Add(Slice(c, c.Min, value.Min - 1));
            }

            if (c.Max > value.Max)
            {
                result.Add(Slice(c, value.Max + 1, c.Max));
            }
        }

        result.Add(value);
        return result.OrderBy(c => c.Min).ToList();
    }

    private static ColumnInfo Slice(ColumnInfo c, int min, int max) => new ColumnInfo(min, max, c.Width, c.CustomWidth, c.Hidden, c.StyleIndex, c.OutlineLevel, c.Collapsed);

    private SheetMetadata GetMetadata(int index, CancellationToken token)
    {
        var part = GetPart(index);
        if (metadata.TryGetValue(index, out var cached))
        {
            return cached;
        }

        var rows = new List<RowInfo>();
        var columns = new List<ColumnInfo>();
        var merges = new List<CellRange>();
        var mergeKeys = new HashSet<string>(StringComparer.Ordinal);
        var elements = new Dictionary<string, ElementInfo>(StringComparer.Ordinal);
        var rowBreaks = new List<ElementInfo>();
        var columnBreaks = new List<ElementInfo>();
        var shared = new Dictionary<int, FormulaMaster>();
        var arrays = new List<FormulaMaster>();
        var dimension = (string?)null;
        var minRow = int.MaxValue;
        var minCol = int.MaxValue;
        var maxRow = 0;
        var maxCol = 0;
        var row = 0;
        var col = 0;
        var drawing = false;
        var links = false;
        var conditional = false;
        var hasFormulas = false;
        var conditionalRanges = new List<IReadOnlyList<CellRange>>();
        var tables = false;
        var breakKind = string.Empty;
        using (var reader = XmlValues.CreateReader(part))
        {
            activeReader = reader;
            while (XmlValues.Read(reader))
            {
                EnsureAlive();
                token.ThrowIfCancellationRequested();
                if (!reader.IsStartElement)
                {
                    continue;
                }

                if (reader.ElementType == typeof(S.Row))
                {
                    var e = AttributesElement(reader);
                    row = NextRow(e, row);
                    col = 0;
                    rows.Add(new RowInfo(row, XmlValues.Double(e, "ht"), XmlValues.Bool(e, "customHeight"), XmlValues.Bool(e, "hidden"), XmlValues.Int(e, "s"), XmlValues.Bool(e, "customFormat"), XmlValues.Int(e, "outlineLevel"), XmlValues.Bool(e, "collapsed")));
                }
                else if (reader.ElementType == typeof(S.Cell))
                {
                    var cell = ReadCell(reader, false, token);
                    var address = NextAddress(cell.Reference, row, col);
                    col = address.Column;
                    minRow = Math.Min(minRow, row);
                    maxRow = Math.Max(maxRow, row);
                    minCol = Math.Min(minCol, col);
                    maxCol = Math.Max(maxCol, col);
                    var formula = cell.Formula;
                    if (formula != null)
                    {
                        hasFormulas = true;
                        var kind = XmlValues.Attr(formula, "t") ?? "normal";
                        var si = XmlValues.Int(formula, "si");
                        var reference = XmlValues.Attr(formula, "ref");
                        if (kind == "shared" && si.HasValue && formula.Value.Length > 0)
                        {
                            if (shared.ContainsKey(si.Value))
                            {
                                throw new InvalidDataException("Duplicate shared formula master.");
                            }

                            shared.Add(si.Value, new FormulaMaster(address, formula.Value, reference, null));
                        }
                        else if (kind == "array" && reference != null)
                        {
                            arrays.Add(new FormulaMaster(address, formula.Value, reference, CellRange.Parse(reference)));
                        }
                    }
                }
                else if (reader.ElementType == typeof(S.ConditionalFormatting))
                {
                    conditional = true;
                    var reference = XmlValues.Attr(AttributesElement(reader), "sqref") ?? string.Empty;
                    conditionalRanges.Add(reference.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(CellRange.Parse).ToArray());
                }
                else if (reader.ElementType == typeof(S.Column))
                {
                    var e = XmlValues.Load(reader);
                    var first = XmlValues.Int(e, "min") ?? 0;
                    var last = XmlValues.Int(e, "max") ?? 0;
                    if (first < 1 || last < first || last > 16384)
                    {
                        throw new InvalidDataException("Invalid column interval.");
                    }

                    columns = OverlayColumn(columns, new ColumnInfo(first, last, XmlValues.Double(e, "width"), XmlValues.Bool(e, "customWidth"), XmlValues.Bool(e, "hidden"), XmlValues.Int(e, "style"), XmlValues.Int(e, "outlineLevel"), XmlValues.Bool(e, "collapsed")));
                }
                else if (reader.ElementType == typeof(S.MergeCell))
                {
                    var e = XmlValues.Load(reader);
                    var range = CellRange.Parse(XmlValues.Attr(e, "ref") ?? string.Empty);
                    if (mergeKeys.Add(range.ToString()))
                    {
                        if (merges.Any(m => m.Intersects(range)))
                        {
                            throw new InvalidDataException("Partially overlapping merge ranges.");
                        }

                        merges.Add(range);
                    }
                }
                else if (reader.LocalName == "dimension")
                {
                    dimension = reader.Attributes.FirstOrDefault(a => a.LocalName == "ref").Value;
                }
                else if (reader.LocalName == "rowBreaks" || reader.LocalName == "colBreaks")
                {
                    breakKind = reader.LocalName;
                }
                else if (reader.LocalName == "brk")
                {
                    (breakKind == "rowBreaks" ? rowBreaks : columnBreaks).Add(XmlValues.Info(XmlValues.Load(reader)));
                }
                else if (reader.LocalName == "sheetFormatPr" || reader.LocalName == "pageSetup" || reader.LocalName == "pageSetUpPr" || reader.LocalName == "pageMargins" || reader.LocalName == "printOptions" || reader.LocalName == "headerFooter")
                {
                    var e = XmlValues.Load(reader);
                    if (e.Name.LocalName == "sheetFormatPr")
                    {
                        XmlValues.Double(e, "defaultRowHeight");
                        XmlValues.Double(e, "defaultColWidth");
                        XmlValues.Int(e, "baseColWidth");
                        XmlValues.Bool(e, "zeroHeight");
                    }

                    elements[e.Name.LocalName] = XmlValues.Info(e);
                }
                else
                {
                    drawing |= reader.LocalName == "drawing" || reader.LocalName == "legacyDrawing";
                    links |= reader.LocalName == "hyperlinks";
                    conditional |= reader.LocalName == "conditionalFormatting";
                    tables |= reader.LocalName == "tableParts";
                }
            }

            activeReader = null;
        }

        ElementInfo? Find(string name) => elements.TryGetValue(name, out var e) ? e : null;
        var print = workbook.DefinedNames.Where(n => n.LocalSheetId == index - 1 && (n.Name == "_xlnm.Print_Area" || n.Name == "_xlnm.Print_Titles")).Select(n => PrintReferences.Parse(n, sheets[index - 1], options.DiagnosticSink)).ToList().AsReadOnly();
        var actual = maxRow == 0 ? null : new CellRange(new CellAddress(minRow, minCol), new CellAddress(maxRow, maxCol));
        var info = new WorksheetInfo(sheets[index - 1], dimension, actual, rows.AsReadOnly(), columns.AsReadOnly(), merges.AsReadOnly(), Find("sheetFormatPr"), Find("pageSetup"), Find("pageSetUpPr"), Find("pageMargins"), Find("printOptions"), Find("headerFooter"), rowBreaks.AsReadOnly(), columnBreaks.AsReadOnly(), print, drawing, links, conditional, tables, conditionalRanges.AsReadOnly(), hasFormulas);
        cached = new SheetMetadata(info, shared, FormulaIndex.Build(arrays));
        metadata.Add(index, cached);
        return cached;
    }

    private sealed class SheetMetadata
    {
        /// <summary>Initializes a new instance of the <see cref="SheetMetadata"/> class.</summary>
        /// <param name="info">The info value.</param>
        /// <param name="shared">The shared value.</param>
        /// <param name="arrays">The arrays value.</param>
        internal SheetMetadata(WorksheetInfo info, Dictionary<int, FormulaMaster> shared, FormulaIndex? arrays)
        {
            Info = info;
            Shared = shared;
            Arrays = arrays;
        }

        /// <summary>Gets the Info metadata.</summary>
        internal WorksheetInfo Info { get; }

        /// <summary>Gets the Shared metadata.</summary>
        internal Dictionary<int, FormulaMaster> Shared { get; }

        /// <summary>Gets the Arrays metadata.</summary>
        internal FormulaIndex? Arrays { get; }
    }

    private sealed class FormulaMaster
    {
        /// <summary>Initializes a new instance of the <see cref="FormulaMaster"/> class.</summary>
        /// <param name="address">The address value.</param>
        /// <param name="text">The text value.</param>
        /// <param name="reference">The reference value.</param>
        /// <param name="range">The range value.</param>
        internal FormulaMaster(CellAddress address, string text, string? reference, CellRange? range)
        {
            Address = address;
            Text = text;
            Reference = reference;
            Range = range;
        }

        /// <summary>Gets the Address metadata.</summary>
        internal CellAddress Address { get; }

        /// <summary>Gets the Text metadata.</summary>
        internal string Text { get; }

        /// <summary>Gets the Reference metadata.</summary>
        internal string? Reference { get; }

        /// <summary>Gets the Range metadata.</summary>
        internal CellRange? Range { get; }
    }

    // Row interval tree: no expanded ranges and no scan of every formula range per cell.
    private sealed class FormulaIndex
    {
        private readonly int center;
        private readonly List<FormulaMaster> byStart;
        private readonly List<FormulaMaster> byEnd;
        private readonly FormulaIndex? left;
        private readonly FormulaIndex? right;

        private FormulaIndex(int center, List<FormulaMaster> spanning, FormulaIndex? left, FormulaIndex? right)
        {
            this.center = center;
            byStart = spanning.OrderBy(m => m.Range!.First.Row).ToList();
            byEnd = spanning.OrderByDescending(m => m.Range!.Last.Row).ToList();
            this.left = left;
            this.right = right;
        }

        /// <summary>Provides Build metadata.</summary>
        /// <returns>The resulting value.</returns>
        /// <param name="ranges">The ranges value.</param>
        internal static FormulaIndex? Build(List<FormulaMaster> ranges)
        {
            if (ranges.Count == 0)
            {
                return null;
            }

            var center = ranges.Select(m => m.Range!.First.Row).OrderBy(r => r).ElementAt(ranges.Count / 2);
            return new FormulaIndex(center, ranges.Where(m => m.Range!.First.Row <= center && m.Range.Last.Row >= center).ToList(), Build(ranges.Where(m => m.Range!.Last.Row < center).ToList()), Build(ranges.Where(m => m.Range!.First.Row > center).ToList()));
        }

        /// <summary>Provides Find metadata.</summary>
        /// <returns>The resulting value.</returns>
        /// <param name="address">The address value.</param>
        internal FormulaMaster? Find(CellAddress address)
        {
            foreach (var master in address.Row <= center ? byStart : byEnd)
            {
                if ((address.Row <= center && master.Range!.First.Row > address.Row) || (address.Row > center && master.Range!.Last.Row < address.Row))
                {
                    break;
                }

                if (master.Range!.Contains(address))
                {
                    return master;
                }
            }

            return address.Row < center ? left?.Find(address) : address.Row > center ? right?.Find(address) : null;
        }
    }
}
