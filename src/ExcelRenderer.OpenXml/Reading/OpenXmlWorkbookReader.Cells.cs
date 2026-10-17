using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using ExcelRenderer.OpenXml.Infrastructure;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Model.Workbook;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml.Reading;

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
            var previousStyle = -1;
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
                    var source = ReadCell(reader, true, token);
                    var address = NextAddress(source.Reference, row, col);
                    col = address.Column;
                    var explicitStyle = source.StyleIndex;
                    var style = explicitStyle ?? rowStyle ?? ColumnStyle(columns, col) ?? 0;
                    if (style != previousStyle)
                    {
                        styles.Resolve(style);
                        previousStyle = style;
                    }

                    var inline = source.Inline;
                    var raw = source.RawValue;
                    var type = source.DataType ?? "n";
                    var sstIndex = (int?)null;
                    var rich = StringEntry.Empty;
                    CellValue value;
                    if (type == "inlineStr")
                    {
                        rich = inline ?? StringEntry.Empty;
                        value = new CellValue(CellValueType.String, rich.Text, null, null);
                    }
                    else if (!source.HasValue)
                    {
                        value = new CellValue(CellValueType.Blank, null, null, null);
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
                        value = new CellValue(CellValueType.String, rich.Text, null, null);
                    }
                    else if (type == "b")
                    {
                        if (raw != "0" && raw != "1")
                        {
                            throw new InvalidDataException("Boolean cell must contain 0 or 1.");
                        }

                        value = new CellValue(CellValueType.Boolean, null, null, raw == "1");
                    }
                    else if (type == "str" || type == "e" || type == "d")
                    {
                        if (type == "d" && (!System.Text.RegularExpressions.Regex.IsMatch(raw!, @"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?)?$") || !DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)))
                        {
                            throw new InvalidDataException("Invalid ISO date cell.");
                        }

                        value = new CellValue(type == "e" ? CellValueType.Error : type == "d" ? CellValueType.IsoDate : CellValueType.String, type == "str" ? XmlValues.Decode(raw!) : raw, null, null);
                    }
                    else if (type == "n")
                    {
                        value = new CellValue(CellValueType.Number, null, XmlValues.Number(raw!), null);
                    }
                    else
                    {
                        throw new InvalidDataException("Unsupported cell value type.");
                    }

                    var f = source.Formula;
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

                        formula = new FormulaInfo(f.Value, kind, si, XmlValues.Attr(f, "ref"), source.HasValue || inline != null, master?.Address, master?.Text, master?.Reference, XmlValues.Attributes(f));
                    }
                    else if ((master = meta.Arrays?.Find(address)) != null)
                    {
                        formula = new FormulaInfo(string.Empty, "array", null, null, source.HasValue || inline != null, master.Address, master.Text, master.Reference, XmlValues.Attributes(XmlValues.Empty("f")));
                    }

                    yield return new CellRecord(index, address, explicitStyle, style, value, raw, formula, sstIndex, rich.Runs, rich.HasPhonetic, source.HasCalculationMetadata);
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

            var entry = ReadString(reader, token, out var characters);
            chars = checked(chars + characters);
            if (chars > options.MaxSharedStringCharacters)
            {
                throw new InvalidDataException("MaxSharedStringCharacters exceeded.");
            }

            result.Add(entry);
        }

        strings = result;
        sharedStringLoadTime = timer.Elapsed;
    }

    private StringEntry ReadString(OpenXmlReader reader, CancellationToken token, out long characters)
    {
        var depth = reader.Depth;
        characters = 0;
        List<RichTextRunInfo>? runs = null;
        string? firstText = null;
        var hasPhonetic = false;
        var inRun = false;
        ElementInfo? properties = null;
        var runText = string.Empty;
        while (XmlValues.Read(reader))
        {
            token.ThrowIfCancellationRequested();
            if (reader.IsEndElement)
            {
                if (reader.Depth == depth)
                {
                    break;
                }

                if (reader.Depth == depth + 1 && reader.ElementType == typeof(S.Run))
                {
                    runs ??= new List<RichTextRunInfo>();
                    if (firstText != null)
                    {
                        runs.Add(new RichTextRunInfo(firstText, null));
                        firstText = null;
                    }

                    runs.Add(new RichTextRunInfo(runText, properties));
                    inRun = false;
                }

                continue;
            }

            if (!reader.IsStartElement)
            {
                continue;
            }

            if (reader.ElementType == typeof(S.Run) && reader.Depth == depth + 1)
            {
                inRun = true;
                properties = null;
                runText = string.Empty;
            }
            else if (reader.ElementType == typeof(S.RunProperties) && inRun)
            {
                var e = XmlValues.Load(reader);
                AddTextCharacters(ref characters, e.Value.Length);
                properties = styles.ColorInfo(e);
            }
            else
            {
                var text = reader.GetText();
                AddTextCharacters(ref characters, text.Length);
                if (reader.ElementType == typeof(S.Text) && (reader.Depth == depth + 1 || (inRun && reader.Depth == depth + 2)))
                {
                    text = XmlValues.Decode(text);
                    if (inRun)
                    {
                        runText += text;
                    }
                    else if (runs != null)
                    {
                        runs.Add(new RichTextRunInfo(text, null));
                    }
                    else if (firstText == null)
                    {
                        firstText = text;
                    }
                    else
                    {
                        firstText += text;
                    }
                }

                hasPhonetic |= reader.ElementType == typeof(S.PhoneticRun) || reader.ElementType == typeof(S.PhoneticProperties);
            }
        }

        if (runs == null)
        {
            var text = firstText ?? string.Empty;
            return new StringEntry(text, text.Length == 0 && firstText == null ? Array.Empty<RichTextRunInfo>() : Array.AsReadOnly(new[] { new RichTextRunInfo(text, null) }), hasPhonetic);
        }

        return new StringEntry(string.Concat(runs.Select(r => r.Text)), runs.AsReadOnly(), hasPhonetic);
    }

    private CellSource ReadCell(OpenXmlReader reader, bool values, CancellationToken token)
    {
        var reference = (string?)null;
        var style = (int?)null;
        var type = (string?)null;
        var hasCalculationMetadata = false;
        foreach (var attribute in reader.Attributes)
        {
            if (attribute.NamespaceUri.Length != 0)
            {
                continue;
            }

            switch (attribute.LocalName)
            {
                case "r": reference = attribute.Value; break;
                case "s":
                    if (!int.TryParse(attribute.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index))
                    {
                        throw new InvalidDataException("Invalid style index.");
                    }

                    style = index;
                    break;
                case "t": type = attribute.Value; break;
                case "cm": case "vm": hasCalculationMetadata = true; break;
            }
        }

        var depth = reader.Depth;
        var raw = (string?)null;
        var hasValue = false;
        XElement? formula = null;
        StringEntry? inline = null;
        long characters = 0;
        while (XmlValues.Read(reader))
        {
            token.ThrowIfCancellationRequested();
            if (reader.IsEndElement && reader.Depth == depth)
            {
                break;
            }

            if (!reader.IsStartElement)
            {
                continue;
            }

            if (reader.ElementType == typeof(S.CellFormula) && reader.Depth == depth + 1)
            {
                formula = XmlValues.Load(reader);
                AddTextCharacters(ref characters, formula.Value.Length);
            }
            else if (reader.ElementType == typeof(S.InlineString) && reader.Depth == depth + 1 && values)
            {
                inline = ReadString(reader, token, out var count);
                AddTextCharacters(ref characters, count);
            }
            else
            {
                var text = reader.GetText();
                AddTextCharacters(ref characters, text.Length);
                if (reader.ElementType == typeof(S.CellValue) && reader.Depth == depth + 1)
                {
                    hasValue = true;
                    raw = text;
                }
            }
        }

        return new CellSource(reference, style, type, raw, hasValue, formula, inline, hasCalculationMetadata);
    }

    private void AddTextCharacters(ref long characters, long added)
    {
        characters = checked(characters + added);
        if (characters > options.MaxCellTextCharacters)
        {
            throw new InvalidDataException("MaxCellTextCharacters exceeded.");
        }
    }

    private readonly struct CellSource
    {
        internal CellSource(string? reference, int? styleIndex, string? dataType, string? rawValue, bool hasValue, XElement? formula, StringEntry? inline, bool hasCalculationMetadata)
        {
            Reference = reference;
            StyleIndex = styleIndex;
            DataType = dataType;
            RawValue = rawValue;
            HasValue = hasValue;
            Formula = formula;
            Inline = inline;
            HasCalculationMetadata = hasCalculationMetadata;
        }

        internal bool HasCalculationMetadata { get; }

        internal string? Reference { get; }

        internal int? StyleIndex { get; }

        internal string? DataType { get; }

        internal string? RawValue { get; }

        internal bool HasValue { get; }

        internal XElement? Formula { get; }

        internal StringEntry? Inline { get; }
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
