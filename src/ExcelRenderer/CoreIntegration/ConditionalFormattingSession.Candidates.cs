using ExcelRenderer.Core.Model;
using ExcelRenderer.Rendering;
using SourceRange = ExcelRenderer.OpenXml.Reader.Model.Cells.CellRange;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Unions finite display candidates and shares ordered rule lists by row/column bands.</summary>
internal sealed partial class ConditionalFormattingSession
{
    private const long CandidateWorkLimit = 16000000;
    private const int CandidateRangeLimit = 2000000;
    private readonly Dictionary<int, RuleRow> ruleRows = [];
    private readonly Dictionary<PreparedRule, IReadOnlyList<SourceRange>> displayRanges = [];
    private int[]? ruleRowStarts;
    private RuleRow? currentRules;
    private long ruleWork;
    private long ruleReferences;

    private static ConditionalFormattingException Limit() => new("ConditionalFormattingLimitExceeded", "The finite candidate/rule normalization work or retained interval budget was exceeded; this sheet retains ordinary formatting.");

    private bool PrepareDisplayRanges(CancellationToken token)
    {
        try
        {
            long retained = 0;
            foreach (var rule in rules)
            {
                token.ThrowIfCancellationRequested();
                ruleWork += rule.Source.Ranges.Count;
                if (ruleWork > CandidateWorkLimit)
                {
                    throw Limit();
                }

                var ranges = rule.Source.Ranges.Count > 1 ? ConditionalPopulation.Coalesce(rule.Source.Ranges, token) : rule.Source.Ranges;
                retained += ranges.Count;
                if (retained > CandidateRangeLimit)
                {
                    throw Limit();
                }

                displayRanges.Add(rule, ranges);
            }

            index = new(displayRanges.SelectMany(pair => pair.Value.Select(range => ((pair.Key, range), (double)range.First.Row, (double)range.Last.Row + 1))));
            return true;
        }
        catch (ConditionalFormattingException exception)
        {
            ReportCandidateLimit(rules[0], info.ConditionalFormattingRules.SelectMany(rule => rule.Ranges).Select(range => new CellAddress(range.First.Row, range.First.Column)), exception.Message);
            return false;
        }
    }

    private bool CollectCandidates(IReadOnlyList<CellRange> regions, HashSet<CellAddress> candidates, long limit, ref long visits, CancellationToken token)
    {
        long work = 0;
        SourceRange? representative = null;
        try
        {
            var source = new HashSet<SourceRange>();
            foreach (var region in regions)
            {
                foreach (var entry in index.Query(region.First.Row, region.Last.Row + 1))
                {
                    Work();
                    var firstColumn = Math.Max(region.First.Column, entry.Range.First.Column);
                    var lastColumn = Math.Min(region.Last.Column, entry.Range.Last.Column);
                    if (firstColumn <= lastColumn)
                    {
                        var range = new SourceRange(new(Math.Max(region.First.Row, entry.Range.First.Row), firstColumn), new(Math.Min(region.Last.Row, entry.Range.Last.Row), lastColumn));
                        representative ??= range;
                        source.Add(range);
                        if (source.Count > CandidateRangeLimit)
                        {
                            throw Limit();
                        }
                    }
                }
            }

            var events = source.SelectMany(range => new[] { (Row: range.First.Row, Add: true, Range: range), (Row: range.Last.Row + 1, Add: false, Range: range) }).OrderBy(item => item.Row).ToArray();
            var active = new HashSet<SourceRange>();
            var normalized = new List<SourceRange>();
            long area = 0;
            for (var position = 0; position < events.Length;)
            {
                token.ThrowIfCancellationRequested();
                var row = events[position].Row;
                do
                {
                    Work();
                    var item = events[position++];
                    if (item.Add)
                    {
                        active.Add(item.Range);
                    }
                    else
                    {
                        active.Remove(item.Range);
                    }
                }
                while (position < events.Length && events[position].Row == row);
                if (position == events.Length || active.Count == 0)
                {
                    continue;
                }

                var intervals = new List<(int First, int Last)>();
                foreach (var range in active)
                {
                    Work();
                    intervals.Add((range.First.Column, range.Last.Column));
                }

                intervals.Sort((left, right) => left.First.CompareTo(right.First));
                var first = intervals[0].First;
                var last = intervals[0].Last;
                foreach (var interval in intervals.Skip(1))
                {
                    if (interval.First > last + 1)
                    {
                        Add();
                        first = interval.First;
                    }

                    last = Math.Max(last, interval.Last);
                }

                Add();

                void Add()
                {
                    Work();
                    area = checked(area + ((long)(events[position].Row - row) * (last - first + 1)));
                    if (area > limit || normalized.Count >= CandidateRangeLimit)
                    {
                        throw Limit();
                    }

                    normalized.Add(new(new(row, first), new(events[position].Row - 1, last)));
                }
            }

            foreach (var range in normalized)
            {
                for (var row = range.First.Row; row <= range.Last.Row; row++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var column = range.First.Column; column <= range.Last.Column; column++)
                    {
                        candidates.Add(new(row, column));
                        visits++;
                    }
                }
            }

            ConversionMetrics.Report("conditionalFormatting.displayCandidateVisits", visits);
            return true;
        }
        catch (ConditionalFormattingException exception)
        {
            if (representative is { } range)
            {
                candidates.Add(new(range.First.Row, range.First.Column));
            }

            ReportCandidateLimit(rules[0], candidates, exception.Message);
            return false;
        }
        finally
        {
            ConversionMetrics.Report("conditionalFormatting.candidateRangeWork", work);
        }

        void Work()
        {
            token.ThrowIfCancellationRequested();
            if (++work > CandidateWorkLimit)
            {
                throw Limit();
            }
        }
    }

    private bool PrepareRuleRows(IReadOnlyList<CellAddress> candidates, CancellationToken token)
    {
        try
        {
            var boundaries = new HashSet<int> { 1 };
            foreach (var range in displayRanges.Values.SelectMany(ranges => ranges))
            {
                token.ThrowIfCancellationRequested();
                if (++ruleWork > CandidateWorkLimit || boundaries.Count > CandidateRangeLimit)
                {
                    throw Limit();
                }

                boundaries.Add(range.First.Row);
                boundaries.Add(range.Last.Row + 1);
            }

            ruleRowStarts = boundaries.OrderBy(value => value).ToArray();
            foreach (var address in candidates)
            {
                EnsureRuleRow(address.Row, token);
            }

            return true;
        }
        catch (ConditionalFormattingException exception)
        {
            ReportCandidateLimit(rules[0], candidates, exception.Message);
            return false;
        }
    }

    private RuleRow EnsureRuleRow(int row, CancellationToken token)
    {
        if (currentRules is { } current && row >= current.First && row <= current.Last)
        {
            return current;
        }

        var starts = ruleRowStarts!;
        var boundary = Array.BinarySearch(starts, row);
        if (boundary < 0)
        {
            boundary = ~boundary - 1;
        }

        if (ruleRows.TryGetValue(boundary, out var cached))
        {
            return currentRules = cached;
        }

        ConversionMetrics.Report("conditionalFormatting.ruleRowPlans", 1);
        ConversionMetrics.Report("conditionalFormatting.ruleListSearches", 1);
        var events = new List<(int Column, PreparedRule Rule, bool Add)>();
        foreach (var entry in index.Query(row, row + 1))
        {
            RuleWork();
            events.Add((entry.Range.First.Column, entry.Rule, true));
            events.Add((entry.Range.Last.Column + 1, entry.Rule, false));
        }

        events.Sort((left, right) => left.Column.CompareTo(right.Column));
        ConversionMetrics.Report("conditionalFormatting.ruleListSorts", 1);
        var counts = new Dictionary<PreparedRule, int>();
        var active = new SortedSet<PreparedRule>(Comparer<PreparedRule>.Create((left, right) =>
        {
            var priority = left.Priority.CompareTo(right.Priority);
            return priority == 0 ? left.Order.CompareTo(right.Order) : priority;
        }));
        var columns = new List<RuleColumn>();
        for (var position = 0; position < events.Count;)
        {
            var first = events[position].Column;
            do
            {
                RuleWork();
                var item = events[position++];
                var count = counts.GetValueOrDefault(item.Rule) + (item.Add ? 1 : -1);
                counts[item.Rule] = count;
                if (count == 0)
                {
                    active.Remove(item.Rule);
                }
                else
                {
                    active.Add(item.Rule);
                }
            }
            while (position < events.Count && events[position].Column == first);
            if (position < events.Count && active.Count > 0)
            {
                ruleReferences += active.Count;
                ruleWork += active.Count;
                RuleWork();
                if (ruleReferences > CandidateRangeLimit)
                {
                    throw Limit();
                }

                columns.Add(new(first, events[position].Column - 1, active.ToArray()));
            }
        }

        var result = new RuleRow(starts[boundary], boundary + 1 < starts.Length ? starts[boundary + 1] - 1 : 1048576, columns.ToArray());
        ruleRows.Add(boundary, result);
        return currentRules = result;

        void RuleWork()
        {
            token.ThrowIfCancellationRequested();
            if (++ruleWork > CandidateWorkLimit)
            {
                throw Limit();
            }
        }
    }

    private IReadOnlyList<PreparedRule> ApplicableRules(CellAddress address, CancellationToken token)
    {
        var columns = EnsureRuleRow(address.Row, token).Columns;
        var low = 0;
        var high = columns.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (columns[middle].Last < address.Column)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < columns.Length && columns[low].First <= address.Column ? columns[low].Rules : [];
    }

    private sealed record RuleColumn(int First, int Last, PreparedRule[] Rules);

    private sealed record RuleRow(int First, int Last, RuleColumn[] Columns);
}
