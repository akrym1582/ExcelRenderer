using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExcelRenderer.OpenXml;

/// <summary>Rewrites A1 reference tokens using final row placements without evaluating expressions.</summary>
internal sealed class StructuralReferences
{
    private static readonly Regex Tokens = new Regex(
        "\"(?:[^\"]|\"\")*\"|(?<![\\w.\\\\$])(?:(?<sheet>'(?:[^']|'')+'|[\\p{L}_\\\\][\\w.\\\\]*)!)?(?<first>\\$?[A-Za-z]{1,3}\\$?[1-9][0-9]*|\\$?[1-9][0-9]*(?=:)|\\$?[A-Za-z]{1,3}(?=:))(?::(?<last>\\$?[A-Za-z]{1,3}\\$?[1-9][0-9]*|\\$?[1-9][0-9]*|\\$?[A-Za-z]{1,3}))?(?![\\w.(])",
        RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, SheetEditPlan> sheets;
    private readonly Dictionary<SheetEditPlan, Dictionary<int, (int First, int Last)>> firstRows = new Dictionary<SheetEditPlan, Dictionary<int, (int First, int Last)>>();
    private readonly Dictionary<(SheetEditPlan Plan, int First, int Last), (int First, int Last)?> ranges = new Dictionary<(SheetEditPlan Plan, int First, int Last), (int First, int Last)?>();

    /// <summary>Initializes a new instance of the <see cref="StructuralReferences"/> class.</summary>
    /// <param name="sheets">Final plans keyed by source worksheet name.</param>
    internal StructuralReferences(IReadOnlyDictionary<string, SheetEditPlan> sheets)
    {
        this.sheets = sheets;
    }

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
            var contexts = occurrences.Select(row => row.OccurrenceId).Distinct().ToArray();
            if (contexts.Length > 1 && contexts.All(context => occurrences.Where(row => row.OccurrenceId == context).Select(row => row.SourceRow).Distinct().Count() == range.Last.Row - range.First.Row + 1))
            {
                foreach (var context in contexts)
                {
                    var rows = occurrences.Where(row => row.OccurrenceId == context).ToArray();
                    Add(rows.Min(row => row.OutputRow), rows.Max(row => row.OutputRow));
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
    /// <returns>Updated expression.</returns>
    internal string Rewrite(string text, string owner, int relativeDelta = 0, bool expandSingleCell = false)
    {
        return Tokens.Replace(text, match =>
        {
            if (text.Substring(match.Index + match.Length).TrimStart().StartsWith("(", StringComparison.Ordinal) || !match.Groups["first"].Success || !sheets.TryGetValue(Sheet(match, owner), out var plan))
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

            var finalSourceRow = expandSingleCell ? Math.Min(lastRow.Value, 1048576 - (plan.Rows.Count - plan.SourceLastRow)) : lastRow.Value;
            var mapped = firstRow.Value > finalSourceRow ? null : Range(plan, firstRow.Value, finalSourceRow);
            if (mapped == null)
            {
                return "#REF!";
            }

            var prefix = match.Groups["sheet"].Success ? match.Groups["sheet"].Value + "!" : string.Empty;
            return prefix + ReplaceRow(first, mapped.Value.First, relativeDelta) + (match.Groups["last"].Success || (expandSingleCell && mapped.Value.Last != mapped.Value.First) ? ":" + ReplaceRow(last, mapped.Value.Last, relativeDelta) : string.Empty);
        });
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

        var rows = plan.Rows.Where(row => row.SourceRow >= first && row.SourceRow <= last).Select(row => row.OutputRow).ToArray();
        var minimum = rows.DefaultIfEmpty(int.MaxValue).Min();
        var maximum = rows.DefaultIfEmpty(0).Max();
        if (last > plan.SourceLastRow)
        {
            minimum = Math.Min(minimum, Point(plan, Math.Max(first, plan.SourceLastRow + 1))!.Value);
            maximum = Math.Max(maximum, Point(plan, last)!.Value);
        }

        (int First, int Last)? result = maximum < minimum ? null : (minimum, maximum);
        ranges.Add(key, result);
        return result;
    }
}
