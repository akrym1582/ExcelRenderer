using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml;

/// <summary>Streams worksheet cells and sparse metadata.</summary>
public sealed partial class OpenXmlWorkbookReader
{
    private static int? ColumnStyle(IReadOnlyList<ColumnInfo> columns, int column)
    {
        var low = 0;
        var high = columns.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var c = columns[middle];
            if (column < c.Min)
            {
                high = middle - 1;
            }
            else if (column > c.Max)
            {
                low = middle + 1;
            }
            else
            {
                return c.StyleIndex;
            }
        }

        return null;
    }

    private IEnumerable<CellRecord> EnumerateCore(int index, CancellationToken token)
    {
        Enter();
        try
        {
            token.ThrowIfCancellationRequested();
            var meta = GetMetadata(index, token);
            using var reader = XmlValues.CreateReader(GetPart(index));
            activeReader = reader;
            var row = 0;
            var col = 0;
            var rowStyle = (int?)null;
            var columns = meta.Info.Columns;
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
                    rowStyle = XmlValues.Bool(e, "customFormat") == true ? XmlValues.Int(e, "s") : null;
                }
                else if (reader.ElementType == typeof(S.Cell))
                {
                    var e = XmlValues.Load(reader);
                    XmlValues.TextLimit(e, options);
                    var address = NextAddress(e, row, col);
                    col = address.Column;
                    var explicitStyle = XmlValues.Int(e, "s");
                    var style = explicitStyle ?? rowStyle ?? ColumnStyle(columns, col) ?? 0;
                    styles.Resolve(style);
                    var v = e.Element(XmlValues.Spreadsheet + "v");
                    var inline = e.Element(XmlValues.Spreadsheet + "is");
                    var raw = v?.Value;
                    var type = XmlValues.Attr(e, "t") ?? "n";
                    var sstIndex = (int?)null;
                    var rich = StringEntry.Empty;
                    CellValue value;
                    if (type == "inlineStr")
                    {
                        rich = inline == null ? StringEntry.Empty : ReadString(inline);
                        value = new CellValue("String", rich.Text, null, null);
                    }
                    else if (v == null)
                    {
                        value = new CellValue("Blank", null, null, null);
                    }
                    else if (type == "s")
                    {
                        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id < 0)
                        {
                            throw new InvalidDataException("Invalid shared string index.");
                        }

                        EnsureStrings(token);
                        if (id >= strings!.Count)
                        {
                            throw new InvalidDataException("Shared string index is outside the table.");
                        }

                        rich = strings[id];
                        sstIndex = id;
                        value = new CellValue("String", rich.Text, null, null);
                    }
                    else if (type == "b")
                    {
                        if (raw != "0" && raw != "1")
                        {
                            throw new InvalidDataException("Boolean cell must contain 0 or 1.");
                        }

                        value = new CellValue("Boolean", null, null, raw == "1");
                    }
                    else if (type == "str" || type == "e" || type == "d")
                    {
                        if (type == "d" && (!System.Text.RegularExpressions.Regex.IsMatch(raw!, @"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?)?$") || !DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)))
                        {
                            throw new InvalidDataException("Invalid ISO date cell.");
                        }

                        value = new CellValue(type == "e" ? "Error" : type == "d" ? "IsoDate" : "String", type == "str" ? XmlValues.Decode(raw!) : raw, null, null);
                    }
                    else if (type == "n")
                    {
                        value = new CellValue("Number", null, XmlValues.Number(raw!), null);
                    }
                    else
                    {
                        throw new InvalidDataException("Unsupported cell value type.");
                    }

                    var f = e.Element(XmlValues.Spreadsheet + "f");
                    var formula = (FormulaInfo?)null;
                    FormulaMaster? master = null;
                    if (f != null)
                    {
                        var kind = XmlValues.Attr(f, "t") ?? "normal";
                        var si = XmlValues.Int(f, "si");
                        if (kind == "shared" && si.HasValue)
                        {
                            meta.Shared.TryGetValue(si.Value, out master);
                        }
                        else if (kind == "array")
                        {
                            master = meta.Arrays?.Find(address);
                        }

                        formula = new FormulaInfo(f.Value, kind, si, XmlValues.Attr(f, "ref"), v != null || inline != null, master?.Address, master?.Text, master?.Reference, XmlValues.Attributes(f));
                    }
                    else if ((master = meta.Arrays?.Find(address)) != null)
                    {
                        formula = new FormulaInfo(string.Empty, "array", null, null, v != null || inline != null, master.Address, master.Text, master.Reference, XmlValues.Attributes(XmlValues.Empty("f")));
                    }

                    yield return new CellRecord(index, address, explicitStyle, style, value, raw, formula, sstIndex, rich.Runs, rich.HasPhonetic);
                    EnsureAlive();
                }
            }
        }
        finally
        {
            activeReader = null;
            Exit();
        }
    }

    private void EnsureStrings(CancellationToken token)
    {
        if (strings != null)
        {
            return;
        }

        var part = document.WorkbookPart!.SharedStringTablePart ?? throw new InvalidDataException("Shared string table is missing.");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = new List<StringEntry>();
        long chars = 0;
        using var reader = XmlValues.CreateReader(part);
        while (XmlValues.Read(reader))
        {
            token.ThrowIfCancellationRequested();
            if (!reader.IsStartElement || reader.ElementType != typeof(S.SharedStringItem))
            {
                continue;
            }

            if (result.Count >= options.MaxSharedStringItems)
            {
                throw new InvalidDataException("MaxSharedStringItems exceeded.");
            }

            var e = XmlValues.Load(reader);
            XmlValues.TextLimit(e, options);
            foreach (var text in e.DescendantNodes().OfType<XText>())
            {
                chars = checked(chars + text.Value.Length);
            }

            if (chars > options.MaxSharedStringCharacters)
            {
                throw new InvalidDataException("MaxSharedStringCharacters exceeded.");
            }

            result.Add(ReadString(e));
        }

        strings = result;
        sharedStringLoadTime = timer.Elapsed;
    }

    private StringEntry ReadString(XElement e)
    {
        var runs = new List<RichTextRunInfo>();
        foreach (var child in e.Elements())
        {
            if (child.Name == XmlValues.Spreadsheet + "t")
            {
                runs.Add(new RichTextRunInfo(XmlValues.Decode(child.Value), null));
            }
            else if (child.Name == XmlValues.Spreadsheet + "r")
            {
                var properties = child.Element(XmlValues.Spreadsheet + "rPr");
                runs.Add(new RichTextRunInfo(string.Concat(child.Elements(XmlValues.Spreadsheet + "t").Select(t => XmlValues.Decode(t.Value))), properties == null ? null : styles.ColorInfo(properties)));
            }
        }

        return new StringEntry(string.Concat(runs.Select(r => r.Text)), runs.AsReadOnly(), e.Elements().Any(c => c.Name.LocalName == "rPh" || c.Name.LocalName == "phoneticPr"));
    }

    private sealed class StringEntry
    {
        /// <summary>Provides StringEntry metadata.</summary>
        internal static readonly StringEntry Empty = new StringEntry(string.Empty, Array.Empty<RichTextRunInfo>(), false);

        /// <summary>Initializes a new instance of the <see cref="StringEntry"/> class.</summary>
        /// <param name="text">The text value.</param>
        /// <param name="runs">The runs value.</param>
        /// <param name="hasPhonetic">The hasPhonetic value.</param>
        internal StringEntry(string text, IReadOnlyList<RichTextRunInfo> runs, bool hasPhonetic)
        {
            Text = text;
            Runs = runs;
            HasPhonetic = hasPhonetic;
        }

        /// <summary>Gets the Text metadata.</summary>
        internal string Text { get; }

        /// <summary>Gets the Runs metadata.</summary>
        internal IReadOnlyList<RichTextRunInfo> Runs { get; }

        /// <summary>Gets a value indicating whether this flag is set.</summary>
        internal bool HasPhonetic { get; }
    }
}
