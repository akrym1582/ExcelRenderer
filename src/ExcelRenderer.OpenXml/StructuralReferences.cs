using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ExcelRenderer.OpenXml;

/// <summary>Rewrites A1 reference tokens using final row placements without evaluating expressions.</summary>
internal sealed class StructuralReferences
{
    private const int TranslatedRuleCacheCapacity = 1024;

    private static readonly Regex Tokens = new Regex(
        "\"(?:[^\"]|\"\")*\"|(?<![\\w.\\\\$])(?:(?<sheet>'(?:[^']|'')+'|[\\p{L}_\\\\][\\w.\\\\]*)!)?(?<first>\\$?[A-Za-z]{1,3}\\$?[1-9][0-9]*|\\$?[1-9][0-9]*(?=:)|\\$?[A-Za-z]{1,3}(?=:))(?::(?<last>\\$?[A-Za-z]{1,3}\\$?[1-9][0-9]*|\\$?[1-9][0-9]*|\\$?[A-Za-z]{1,3}))?(?![\\w.(])",
        RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, SheetEditPlan> sheets;
    private readonly Dictionary<(string Text, int Row, int Column), string> translatedRules = new Dictionary<(string Text, int Row, int Column), string>();
    private readonly Dictionary<SheetEditPlan, Dictionary<(int Context, int Row), int>> contextRows = new Dictionary<SheetEditPlan, Dictionary<(int Context, int Row), int>>();
    private readonly Dictionary<SheetEditPlan, RowRangeIndex> rangeIndexes = new Dictionary<SheetEditPlan, RowRangeIndex>();
    private readonly Dictionary<SheetEditPlan, Dictionary<int, int>> parents = new Dictionary<SheetEditPlan, Dictionary<int, int>>();
    private readonly Dictionary<SheetEditPlan, Dictionary<int, (int First, int Last)>> firstRows = new Dictionary<SheetEditPlan, Dictionary<int, (int First, int Last)>>();
    private readonly Dictionary<(SheetEditPlan Plan, int First, int Last), (int First, int Last)?> ranges = new Dictionary<(SheetEditPlan Plan, int First, int Last), (int First, int Last)?>();

    /// <summary>Initializes a new instance of the <see cref="StructuralReferences"/> class.</summary>
    /// <param name="sheets">Final plans keyed by source worksheet name.</param>
    internal StructuralReferences(IReadOnlyDictionary<string, SheetEditPlan> sheets)
    {
        this.sheets = sheets;
        HasRowChanges = sheets.Values.Any(plan => plan.Rows.Count != plan.SourceLastRow || plan.Rows.Any(row => row.SourceRow != row.OutputRow));
    }

    /// <summary>Gets a value indicating whether any source row placement changes.</summary>
    internal bool HasRowChanges { get; }

    /// <summary>Checks whether a structural formula uses only the supported reference grammar.</summary>
    /// <param name="text">Saved structural formula.</param>
    /// <returns>Whether reference tokens can be rewritten safely.</returns>
    internal static bool CanRewrite(string text)
    {
        var unquoted = Regex.Replace(text, "\"(?:[^\"]|\"\")*\"", string.Empty);
        return unquoted.IndexOfAny(new[] { '[', ']', '{', '}' }) < 0 &&
            !Regex.IsMatch(unquoted, "(?:'[^']*'|[\\w.]+):(?:'[^']*'|[\\w.]+)!") &&
            !Regex.IsMatch(unquoted, "[A-Za-z0-9)]#");
    }

    /// <summary>Finds row intervals addressed on a worksheet.</summary>
    /// <param name="text">Static reference expression.</param>
    /// <param name="sheet">Referenced worksheet.</param>
    /// <param name="owner">Scope for unqualified references.</param>
    /// <returns>Source row intervals.</returns>
    internal static IEnumerable<(int First, int Last)> Rows(string text, string sheet, string owner)
    {
        foreach (Match match in Tokens.Matches(text))
        {
            if (!match.Groups["first"].Success || !string.Equals(Sheet(match, owner), sheet, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var first = Row(match.Groups["first"].Value);
            var last = match.Groups["last"].Success ? Row(match.Groups["last"].Value) : first;
            yield return (first ?? 1, last ?? 1048576);
        }
    }

    /// <summary>Expands a range into its surviving output occurrences.</summary>
    /// <param name="reference">Space separated cell ranges.</param>
    /// <param name="plan">Final row placements.</param>
    /// <returns>Output ranges and their source and destination anchors.</returns>
    internal static IReadOnlyList<(string Reference, int SourceRow, int OutputRow)> Expand(string reference, SheetEditPlan plan)
    {
        var result = new List<(string Reference, int SourceRow, int OutputRow)>();
        foreach (var token in reference.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var range = CellRange.Parse(token.Replace("$", string.Empty));
            var occurrences = plan.Rows.Where(row => row.SourceRow >= range.First.Row && row.SourceRow <= range.Last.Row).ToArray();
            var contexts = occurrences.GroupBy(row => row.OccurrenceId).Select(group => group.ToArray()).ToArray();
            if (contexts.Length > 1 && contexts.All(rows => rows.Select(row => row.SourceRow).Distinct().Count() == range.Last.Row - range.First.Row + 1))
            {
                foreach (var context in contexts)
                {
                    Add(context.Min(row => row.OutputRow), context.Max(row => row.OutputRow));
                }
            }
            else
            {
                var first = 0;
                var last = 0;
                foreach (var row in occurrences.Select(item => item.OutputRow).OrderBy(row => row))
                {
                    if (first > 0 && row != last + 1)
                    {
                        Add(first, last);
                        first = 0;
                    }

                    if (first == 0)
                    {
                        first = row;
                    }

                    last = row;
                }

                if (range.Last.Row > plan.SourceLastRow)
                {
                    var trailing = Point(plan, Math.Max(range.First.Row, plan.SourceLastRow + 1))!.Value;
                    if (first > 0 && trailing != last + 1)
                    {
                        Add(first, last);
                        first = 0;
                    }

                    first = first == 0 ? trailing : first;
                    last = Point(plan, range.Last.Row)!.Value;
                }

                if (first > 0)
                {
                    Add(first, last);
                }
            }

            void Add(int first, int last) => result.Add((new CellAddress(first, range.First.Column) + (first == last && range.First.Column == range.Last.Column ? string.Empty : ":" + new CellAddress(last, range.Last.Column)), range.First.Row, first));
        }

        return result;
    }

    /// <summary>Finds the first surviving output location of a source row.</summary>
    /// <param name="plan">Final row placements.</param>
    /// <param name="row">Source row.</param>
    /// <returns>The output row, or null for deletion.</returns>
    internal static int? Point(SheetEditPlan plan, int row)
    {
        if (row > plan.SourceLastRow)
        {
            return CheckedRow(checked(row + plan.Rows.Count - plan.SourceLastRow));
        }

        return plan.Rows.FirstOrDefault(item => item.SourceRow == row)?.OutputRow;
    }

    /// <summary>Enumerates the same standard formula stores for preflight and writing.</summary>
    /// <param name="root">Structural rule or worksheet metadata.</param>
    /// <returns>Formula element text and formula-valued threshold attributes.</returns>
    internal static IEnumerable<XObject> FormulaStores(XElement root)
    {
        foreach (var element in root.DescendantsAndSelf())
        {
            if (IsFormulaElement(element.Name.LocalName, element.Name.NamespaceName))
            {
                yield return element;
            }
            else if (IsFormulaThreshold(element.Name.LocalName, element.Name.NamespaceName, (string?)element.Attribute("type")) && element.Attribute("val") is { } value)
            {
                yield return value;
            }
        }
    }

    /// <summary>Reads a formula store at the current streaming reader position.</summary>
    /// <param name="reader">Reader positioned at a start element.</param>
    /// <returns>Formula text, or null when the element is not a formula store.</returns>
    internal static string? ReadFormulaStore(XmlReader reader)
    {
        if (IsFormulaThreshold(reader.LocalName, reader.NamespaceURI, reader.GetAttribute("type")))
        {
            return reader.GetAttribute("val");
        }

        if (!IsFormulaElement(reader.LocalName, reader.NamespaceURI))
        {
            return null;
        }

        using var formula = reader.ReadSubtree();
        formula.Read();
        return formula.ReadElementContentAsString();
    }

    /// <summary>Reads a structural formula without distinguishing its XML storage.</summary>
    /// <param name="store">Formula element or attribute.</param>
    /// <returns>Saved formula text.</returns>
    internal static string FormulaText(XObject store) => store is XAttribute attribute ? attribute.Value : ((XElement)store).Value;

    /// <summary>Writes a structural formula back to its original XML storage.</summary>
    /// <param name="store">Formula element or attribute.</param>
    /// <param name="text">Rewritten formula text.</param>
    internal static void SetFormulaText(XObject store, string text)
    {
        if (store is XAttribute attribute)
        {
            attribute.Value = text;
        }
        else
        {
            ((XElement)store).Value = text;
        }
    }

    /// <summary>Translates relative coordinates, separately from structural row movement.</summary>
    /// <param name="text">Saved rule expression.</param>
    /// <param name="rowDelta">Copy translation in source rows.</param>
    /// <param name="columnDelta">Copy translation in columns.</param>
    /// <returns>Translated expression.</returns>
    internal static string Translate(string text, int rowDelta, int columnDelta)
    {
        if (rowDelta == 0 && columnDelta == 0)
        {
            return text;
        }

        return Tokens.Replace(text, match =>
        {
            if (!IsReference(text, match))
            {
                return match.Value;
            }

            var prefix = match.Groups["sheet"].Success ? match.Groups["sheet"].Value + "!" : string.Empty;
            return prefix + Move(match.Groups["first"].Value) + (match.Groups["last"].Success ? ":" + Move(match.Groups["last"].Value) : string.Empty);
        });

        string Move(string reference)
        {
            var row = Row(reference);
            var moved = row is { } value ? ReplaceRow(reference, value, rowDelta) : reference;
            var letters = Column(reference);
            if (letters.Length == 0 || reference[0] == '$' || columnDelta == 0)
            {
                return moved;
            }

            var column = letters.Value + columnDelta;
            if (column is < 1 or > 16384)
            {
                throw new NotSupportedException("A structural reference moves outside the Excel column grid.");
            }

            var address = new CellAddress(1, column).ToString();
            return address.Substring(0, address.Length - 1) + moved.Substring(letters.Length);
        }
    }

    /// <summary>Identifies expressions with the same relative meaning at different anchors.</summary>
    /// <param name="text">Rewritten expression.</param>
    /// <param name="anchor">Output rule anchor.</param>
    /// <returns>A comparison key, never an Excel expression.</returns>
    internal static string RuleKey(string text, CellAddress anchor)
    {
        return Tokens.Replace(text, match =>
        {
            if (!IsReference(text, match))
            {
                return match.Value;
            }

            return (match.Groups["sheet"].Success ? match.Groups["sheet"].Value + "!" : string.Empty) + Key(match.Groups["first"].Value) + (match.Groups["last"].Success ? ":" + Key(match.Groups["last"].Value) : string.Empty);
        });

        string Key(string reference)
        {
            var row = Row(reference);
            var letters = Column(reference);
            var column = letters.Value;
            var fixedRow = IsFixedRow(reference);
            return "[" + (reference[0] == '$' && letters.Length > 0 ? "C" : "c") + (letters.Length > 0 ? column - (reference[0] == '$' ? 0 : anchor.Column) : 0).ToString(CultureInfo.InvariantCulture) +
                (fixedRow ? "R" : "r") + (row is { } value ? value - (fixedRow ? 0 : anchor.Row) : 0).ToString(CultureInfo.InvariantCulture) + "]";
        }
    }

    /// <summary>Finds the first output row using a per-write source-row index.</summary>
    /// <param name="plan">Final row placements.</param>
    /// <param name="row">Original row.</param>
    /// <returns>The first surviving row, or null for deletion.</returns>
    internal int? FirstRow(SheetEditPlan plan, int row)
    {
        if (row > plan.SourceLastRow)
        {
            return CheckedRow(checked(row + plan.Rows.Count - plan.SourceLastRow));
        }

        if (!firstRows.TryGetValue(plan, out var index))
        {
            index = new Dictionary<int, (int First, int Last)>();
            foreach (var occurrence in plan.Rows)
            {
                index[occurrence.SourceRow] = index.TryGetValue(occurrence.SourceRow, out var previous) ?
                    (previous.First, occurrence.OutputRow) : (occurrence.OutputRow, occurrence.OutputRow);
            }

            firstRows.Add(plan, index);
        }

        return index.TryGetValue(row, out var output) ? output.First : null;
    }

    /// <summary>Finds the repeat grouping only for source rows that actually repeat.</summary>
    /// <param name="plan">Final row placements.</param>
    /// <param name="sourceRow">Source application row.</param>
    /// <param name="context">Repeat context.</param>
    /// <returns>Context identity or zero for a single surviving occurrence.</returns>
    internal int RuleContext(SheetEditPlan plan, int sourceRow, int context)
    {
        FirstRow(plan, sourceRow);
        return sourceRow <= plan.SourceLastRow && firstRows[plan].TryGetValue(sourceRow, out var row) && row.First != row.Last ? context : 0;
    }

    /// <summary>Preserves the unique-row restriction on ordinary static names.</summary>
    /// <param name="text">Static name definition.</param>
    /// <param name="owner">Scope of unqualified references.</param>
    internal void ValidateName(string text, string owner)
    {
        foreach (var sheet in sheets)
        {
            foreach (var range in Rows(text, sheet.Key, owner))
            {
                var rows = sheet.Value.Rows.Where(row => row.SourceRow >= range.First && row.SourceRow <= range.Last).ToArray();
                var expected = Math.Max(0, Math.Min(range.Last, sheet.Value.SourceLastRow) - range.First + 1);
                if (rows.Length != expected || rows.Any(row => row.OccurrenceId != 0) || rows.Select(row => row.SourceRow).Distinct().Count() != expected)
                {
                    throw new NotSupportedException("A defined name intersects repeated or deleted rows.");
                }
            }
        }
    }

    /// <summary>Rewrites reference tokens while preserving quoted string literals.</summary>
    /// <param name="text">Reference or structural formula text.</param>
    /// <param name="owner">Scope for unqualified references.</param>
    /// <param name="relativeDelta">Relative translation for duplicated rules.</param>
    /// <param name="expandSingleCell">Whether a print-area cell includes every occurrence.</param>
    /// <param name="contextPlan">Worksheet for matching repeat contexts.</param>
    /// <param name="context">Repeat context for relative references.</param>
    /// <returns>Updated expression.</returns>
    internal string Rewrite(string text, string owner, int relativeDelta = 0, bool expandSingleCell = false, SheetEditPlan? contextPlan = null, int context = 0)
    {
        return Tokens.Replace(text, match =>
        {
            if (!IsReference(text, match) || !sheets.TryGetValue(Sheet(match, owner), out var plan))
            {
                return match.Value;
            }

            var first = match.Groups["first"].Value;
            var last = match.Groups["last"].Success ? match.Groups["last"].Value : first;
            var firstRow = Row(first);
            var lastRow = Row(last);
            if (firstRow == null || lastRow == null)
            {
                return match.Value;
            }

            var contextualFirst = plan == contextPlan && context != 0 && !IsFixedRow(first) ? ContextRow(plan, firstRow.Value, context) : null;
            var contextualLast = plan == contextPlan && context != 0 && !IsFixedRow(last) ? ContextRow(plan, lastRow.Value, context) : null;
            var finalSourceRow = expandSingleCell ? Math.Min(lastRow.Value, 1048576 - (plan.Rows.Count - plan.SourceLastRow)) : lastRow.Value;
            var mapped = firstRow.Value > finalSourceRow ? null : Range(plan, firstRow.Value, finalSourceRow);
            if (mapped == null)
            {
                return "#REF!";
            }

            var prefix = match.Groups["sheet"].Success ? match.Groups["sheet"].Value + "!" : string.Empty;
            return prefix + ReplaceRow(first, contextualFirst ?? mapped.Value.First, contextualFirst == null ? relativeDelta : 0) + (match.Groups["last"].Success || (expandSingleCell && mapped.Value.Last != mapped.Value.First) ? ":" + ReplaceRow(last, contextualLast ?? mapped.Value.Last, contextualLast == null ? relativeDelta : 0) : string.Empty);
        });
    }

    /// <summary>Rebases a rule at one surviving source cell and its output occurrence.</summary>
    /// <param name="text">Expression at the original sqref base.</param>
    /// <param name="owner">Rule worksheet.</param>
    /// <param name="originalBase">Original sqref base.</param>
    /// <param name="source">Source application cell.</param>
    /// <param name="output">Surviving output application cell.</param>
    /// <param name="context">Repeat context identity.</param>
    /// <returns>Expression at the output cell.</returns>
    internal string RewriteRule(string text, string owner, CellAddress originalBase, CellAddress source, CellAddress output, int context)
    {
        var key = (text, source.Row - originalBase.Row, source.Column - originalBase.Column);
        if (!translatedRules.TryGetValue(key, out var translated))
        {
            translated = Translate(text, key.Item2, key.Item3);
            if (translatedRules.Count < TranslatedRuleCacheCapacity)
            {
                translatedRules.Add(key, translated);
            }
        }

        var ownerPlan = sheets[owner];
        var delta = output.Row - (FirstRow(ownerPlan, source.Row) ?? output.Row);
        return Rewrite(translated, owner, delta, contextPlan: ownerPlan, context: context);
    }

    /// <summary>Finds the last implicit source row whose relative targets may cross planned rows.</summary>
    /// <param name="text">Original rule expression.</param>
    /// <param name="owner">Rule worksheet.</param>
    /// <param name="originalBase">Original sqref anchor.</param>
    /// <returns>The last row requiring individual placement inspection.</returns>
    internal int RuleTailBoundary(string text, string owner, CellAddress originalBase)
    {
        var boundary = 0;
        foreach (Match match in Tokens.Matches(text))
        {
            if (!IsReference(text, match) || !sheets.TryGetValue(Sheet(match, owner), out var plan))
            {
                continue;
            }

            foreach (var reference in new[] { match.Groups["first"].Value, match.Groups["last"].Value })
            {
                if (Row(reference) is { } row && !IsFixedRow(reference))
                {
                    boundary = Math.Max(boundary, plan.SourceLastRow + originalBase.Row - row);
                }
            }
        }

        return boundary;
    }

    private static bool IsFormulaElement(string localName, string ns) => ns == XmlValues.Spreadsheet.NamespaceName && localName is "formula" or "formula1" or "formula2";

    private static bool IsFormulaThreshold(string localName, string ns, string? type) => ns == XmlValues.Spreadsheet.NamespaceName && localName == "cfvo" && type == "formula";

    private static bool IsReference(string text, Match match)
    {
        if (!match.Groups["first"].Success)
        {
            return false;
        }

        var following = match.Index + match.Length;
        while (following < text.Length && char.IsWhiteSpace(text[following]))
        {
            following++;
        }

        return following == text.Length || text[following] != '(';
    }

    private static bool IsFixedRow(string reference)
    {
        var index = reference.LastIndexOf('$');
        return index >= 0 && index + 1 < reference.Length && char.IsDigit(reference[index + 1]);
    }

    private static (int Length, int Value) Column(string reference)
    {
        var start = reference.Length > 0 && reference[0] == '$' ? 1 : 0;
        var index = start;
        var value = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            value = (value * 26) + char.ToUpperInvariant(reference[index++]) - 'A' + 1;
        }

        return (index - start, value);
    }

    private static string Sheet(Match match, string owner)
    {
        var sheet = match.Groups["sheet"].Value;
        return sheet.Length == 0 ? owner : sheet[0] == '\'' ? sheet.Substring(1, sheet.Length - 2).Replace("''", "'") : sheet;
    }

    private static int? Row(string reference)
    {
        var start = reference.Length;
        while (start > 0 && char.IsDigit(reference[start - 1]))
        {
            start--;
        }

        return start == reference.Length ? null : CheckedRow(int.Parse(reference.Substring(start), CultureInfo.InvariantCulture));
    }

    private static string ReplaceRow(string reference, int row, int delta)
    {
        var start = reference.Length;
        while (start > 0 && char.IsDigit(reference[start - 1]))
        {
            start--;
        }

        return reference.Substring(0, start) + CheckedRow(checked(row + (start > 0 && reference[start - 1] == '$' ? 0 : delta))).ToString(CultureInfo.InvariantCulture);
    }

    private static int CheckedRow(int row) => row is < 1 or > 1048576 ? throw new NotSupportedException("A structural reference moves outside the Excel row grid.") : row;

    private int? ContextRow(SheetEditPlan plan, int row, int context)
    {
        if (!contextRows.TryGetValue(plan, out var rows))
        {
            rows = new Dictionary<(int Context, int Row), int>();
            var ancestors = new Dictionary<int, int>();
            foreach (var occurrence in plan.Rows)
            {
                rows[(occurrence.OccurrenceId, occurrence.SourceRow)] = occurrence.OutputRow;
                ancestors[occurrence.OccurrenceId] = occurrence.ParentOccurrenceId;
            }

            contextRows.Add(plan, rows);
            parents.Add(plan, ancestors);
        }

        while (context != 0)
        {
            if (rows.TryGetValue((context, row), out var output))
            {
                return output;
            }

            context = parents[plan].GetValueOrDefault(context);
        }

        return null;
    }

    private (int First, int Last)? Range(SheetEditPlan plan, int first, int last)
    {
        var key = (plan, first, last);
        if (ranges.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (first == last)
        {
            var row = FirstRow(plan, first);
            (int First, int Last)? point = row is { } output ? (output, first <= plan.SourceLastRow ? firstRows[plan][first].Last : output) : null;
            ranges.Add(key, point);
            return point;
        }

        if (!rangeIndexes.TryGetValue(plan, out var index))
        {
            FirstRow(plan, 1);
            index = new RowRangeIndex(firstRows.TryGetValue(plan, out var placements) ? placements : new Dictionary<int, (int First, int Last)>());
            rangeIndexes.Add(plan, index);
        }

        var (minimum, maximum) = index.Find(first, last);
        if (last > plan.SourceLastRow)
        {
            minimum = Math.Min(minimum, Point(plan, Math.Max(first, plan.SourceLastRow + 1))!.Value);
            maximum = Math.Max(maximum, Point(plan, last)!.Value);
        }

        (int First, int Last)? result = maximum < minimum ? null : (minimum, maximum);
        ranges.Add(key, result);
        return result;
    }

    private sealed class RowRangeIndex
    {
        private readonly int[] sourceRows;
        private readonly int[] minimum;
        private readonly int[] maximum;
        private readonly int leafCount;

        internal RowRangeIndex(Dictionary<int, (int First, int Last)> rows)
        {
            var entries = rows.OrderBy(row => row.Key).ToArray();
            sourceRows = entries.Select(row => row.Key).ToArray();
            leafCount = 1;
            while (leafCount < entries.Length)
            {
                leafCount *= 2;
            }

            minimum = new int[leafCount * 2];
            maximum = new int[leafCount * 2];
            Array.Fill(minimum, int.MaxValue);
            for (var index = 0; index < entries.Length; index++)
            {
                minimum[leafCount + index] = entries[index].Value.First;
                maximum[leafCount + index] = entries[index].Value.Last;
            }

            for (var index = leafCount - 1; index > 0; index--)
            {
                minimum[index] = Math.Min(minimum[index * 2], minimum[(index * 2) + 1]);
                maximum[index] = Math.Max(maximum[index * 2], maximum[(index * 2) + 1]);
            }
        }

        internal (int Minimum, int Maximum) Find(int first, int last)
        {
            var left = Array.BinarySearch(sourceRows, first);
            var right = Array.BinarySearch(sourceRows, last + 1);
            left = (left < 0 ? ~left : left) + leafCount;
            right = (right < 0 ? ~right : right) + leafCount;
            var min = int.MaxValue;
            var max = 0;
            while (left < right)
            {
                if ((left & 1) != 0)
                {
                    min = Math.Min(min, minimum[left]);
                    max = Math.Max(max, maximum[left++]);
                }

                if ((right & 1) != 0)
                {
                    min = Math.Min(min, minimum[--right]);
                    max = Math.Max(max, maximum[right]);
                }

                left /= 2;
                right /= 2;
            }

            return (min, max);
        }
    }
}
