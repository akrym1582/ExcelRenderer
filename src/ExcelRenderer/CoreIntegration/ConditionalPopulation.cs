using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Calculation;
using SourceRange = ExcelRenderer.OpenXml.Reader.Model.Cells.CellRange;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Conversion-local sparse populations and lazily shared numeric/frequency statistics.</summary>
internal sealed class ConditionalPopulation
{
    /// <summary>Maximum total stored value visits and retained values per worksheet.</summary>
    internal const int StoredCellLimit = 2000000;

    /// <summary>Maximum source rectangles before linear coalescing.</summary>
    internal const int RangeLimit = 100000;

    /// <summary>Maximum remaining rectangles entering the bounded union sweep.</summary>
    internal const int SweepRangeLimit = 4096;

    /// <summary>Maximum disjoint output rectangles per population.</summary>
    internal const int NormalizedRangeLimit = 65536;

    /// <summary>Maximum range inspections per normalization.</summary>
    internal const int RangeWorkLimit = 1000000;
    private readonly List<FormulaValue> values = [];
    private readonly long blanks;
    private List<double>? numbers;
    private Dictionary<FormulaValue, long>? frequencies;
    private bool sorted;
    private (double Mean, double Sigma)? statistics;

    private ConditionalPopulation(IReadOnlyList<SourceRange> ranges, WorkbookCalculationSession calculation, string sheet, Action visit, CancellationToken token)
    {
        long area = 0;
        foreach (var range in ranges)
        {
            area = checked(area + checked((long)(range.Last.Row - range.First.Row + 1) * (range.Last.Column - range.First.Column + 1)));
            foreach (var address in calculation.EnumerateValueCells(new(sheet, range.First.Row, range.First.Column, range.Last.Row, range.Last.Column), token))
            {
                token.ThrowIfCancellationRequested();
                visit();
                values.Add(calculation.ReadValue(address));
            }
        }

        // Explicit empty cells omitted by the value index are true blanks too.
        blanks = checked(area - values.Count);
        Core.Rendering.ConversionMetrics.Report("conditionalFormatting.populations", 1);
    }

    /// <summary>Gets prepared numeric values, sorted only when requested.</summary>
    internal IReadOnlyList<double> Numbers
    {
        get
        {
            if (numbers is null)
            {
                throw new InvalidOperationException("Numeric statistics must be prepared before use.");
            }

            return numbers;
        }
    }

    /// <summary>Gets the minimum prepared numeric value, or positive infinity for an empty numeric population.</summary>
    internal double Minimum { get; private set; } = double.PositiveInfinity;

    /// <summary>Gets the maximum prepared numeric value, or negative infinity for an empty numeric population.</summary>
    internal double Maximum { get; private set; } = double.NegativeInfinity;

    /// <summary>Builds a bounded disjoint rectangle union without visiting missing coordinates.</summary>
    /// <param name="source">Saved sqrefs.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>The conversion-local cache key and disjoint rectangles.</returns>
    internal static (string Key, IReadOnlyList<SourceRange> Ranges) Normalize(IReadOnlyList<SourceRange> source, CancellationToken token)
    {
        if (source.Count > RangeLimit)
        {
            throw new ConditionalFormattingException("ConditionalFormattingLimitExceeded", "Population source range count exceeds 100000.");
        }

        source = Coalesce(source, token);
        if (source.Count > SweepRangeLimit)
        {
            throw new ConditionalFormattingException("ConditionalFormattingLimitExceeded", "Population sweep range count exceeds 4096 after coalescing.");
        }

        token.ThrowIfCancellationRequested();
        var boundaries = source.SelectMany(range => new[] { range.First.Row, range.Last.Row + 1 }).Distinct().OrderBy(row => row).ToArray();
        var result = new List<SourceRange>();
        var work = 0;
        var previous = new Dictionary<(int First, int Last), int>();
        for (var index = 0; index + 1 < boundaries.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var row = boundaries[index];
            var intervals = new List<(int First, int Last)>();
            foreach (var range in source)
            {
                token.ThrowIfCancellationRequested();
                if (++work > RangeWorkLimit)
                {
                    throw new ConditionalFormattingException("ConditionalFormattingLimitExceeded", "Population normalization exceeds 1000000 range inspections.");
                }

                if (range.First.Row <= row && range.Last.Row >= row)
                {
                    intervals.Add((range.First.Column, range.Last.Column));
                }
            }

            intervals.Sort((left, right) => left.First.CompareTo(right.First));
            token.ThrowIfCancellationRequested();
            var first = 0;
            var last = 0;
            foreach (var interval in intervals)
            {
                if (first != 0 && interval.First > last + 1)
                {
                    Add();
                    first = 0;
                }

                first = first == 0 ? interval.First : first;
                last = Math.Max(last, interval.Last);
            }

            if (first != 0)
            {
                Add();
            }

            void Add()
            {
                if (previous.TryGetValue((first, last), out var position) && result[position].Last.Row == row - 1)
                {
                    result[position] = new(result[position].First, new(boundaries[index + 1] - 1, last));
                    return;
                }

                if (result.Count >= NormalizedRangeLimit)
                {
                    throw new ConditionalFormattingException("ConditionalFormattingLimitExceeded", "Population normalization exceeds 65536 rectangles.");
                }

                previous[(first, last)] = result.Count;
                result.Add(new(new(row, first), new(boundaries[index + 1] - 1, last)));
            }
        }

        result = result.OrderBy(range => range.First.Row).ThenBy(range => range.First.Column).ToList();
        return (string.Join(" ", result.Select(range => range.ToString())), result);
    }

    /// <summary>Coalesces adjacent equal-width/height rectangles without changing a rule's source anchors.</summary>
    /// <param name="source">The bounded source metadata.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>Equivalent union geometry for display or population normalization.</returns>
    internal static IReadOnlyList<SourceRange> Coalesce(IReadOnlyList<SourceRange> source, CancellationToken token)
    {
        // Mapping can save a separate sqref for every expanded row. Merge those
        // intervals before the general sweep, without allocating any missing cells.
        return Merge(Merge(source, true), false);

        List<SourceRange> Merge(IReadOnlyList<SourceRange> input, bool vertical)
        {
            token.ThrowIfCancellationRequested();
            var sortedRanges = input.ToArray();
            Array.Sort(sortedRanges, (left, right) =>
            {
                var first = (vertical ? left.First.Column : left.First.Row).CompareTo(vertical ? right.First.Column : right.First.Row);
                var last = (vertical ? left.Last.Column : left.Last.Row).CompareTo(vertical ? right.Last.Column : right.Last.Row);
                return first != 0 ? first : last != 0 ? last :
                    (vertical ? left.First.Row : left.First.Column).CompareTo(vertical ? right.First.Row : right.First.Column);
            });
            token.ThrowIfCancellationRequested();
            var result = new List<SourceRange>();
            foreach (var range in sortedRanges)
            {
                token.ThrowIfCancellationRequested();
                var previous = result.Count == 0 ? null : result[result.Count - 1];
                if (previous is not null && (vertical
                    ? previous.First.Column == range.First.Column && previous.Last.Column == range.Last.Column && range.First.Row <= previous.Last.Row + 1
                    : previous.First.Row == range.First.Row && previous.Last.Row == range.Last.Row && range.First.Column <= previous.Last.Column + 1))
                {
                    result[result.Count - 1] = vertical
                        ? new(previous.First, new(Math.Max(previous.Last.Row, range.Last.Row), previous.Last.Column))
                        : new(previous.First, new(previous.Last.Row, Math.Max(previous.Last.Column, range.Last.Column)));
                }
                else
                {
                    result.Add(range);
                }
            }

            return result;
        }
    }

    /// <summary>Reads each stored coordinate in the union exactly once.</summary>
    /// <param name="ranges">Disjoint normalized rectangles.</param>
    /// <param name="calculation">Shared saved/recalculated snapshot.</param>
    /// <param name="sheet">Source sheet.</param>
    /// <param name="visit">Shared worksheet budget accounting.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>The complete population, or an evaluation failure.</returns>
    internal static ConditionalPopulation Create(IReadOnlyList<SourceRange> ranges, WorkbookCalculationSession calculation, string sheet, Action visit, CancellationToken token) => new(ranges, calculation, sheet, visit, token);

    /// <summary>Tests a computed statistic for nonfinite arithmetic.</summary>
    /// <param name="value">Computed value.</param>
    /// <returns>Whether the value is finite.</returns>
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>Materializes numeric statistics once and sorts at most once.</summary>
    /// <param name="sort">Whether rank or percentile needs sorted values.</param>
    /// <param name="token">Cooperative cancellation.</param>
    internal void PrepareNumbers(bool sort, CancellationToken token)
    {
        if (numbers is null)
        {
            numbers = [];
            foreach (var value in values)
            {
                token.ThrowIfCancellationRequested();
                if (value.Kind == FormulaValueKind.Number)
                {
                    numbers.Add(value.Number);
                    Minimum = Math.Min(Minimum, value.Number);
                    Maximum = Math.Max(Maximum, value.Number);
                }
            }
        }

        if (sort && !sorted)
        {
            token.ThrowIfCancellationRequested();
            numbers.Sort();
            token.ThrowIfCancellationRequested();
            sorted = true;
            Core.Rendering.ConversionMetrics.Report("conditionalFormatting.sorts", 1);
        }
    }

    /// <summary>Counts typed values using ordinal case-insensitive text equality.</summary>
    /// <param name="value">Cell value.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>Frequency including implicit blanks.</returns>
    internal long Frequency(FormulaValue value, CancellationToken token)
    {
        if (frequencies is null)
        {
            frequencies = new(new ValueComparer());
            frequencies.Add(FormulaValue.Blank, blanks);
            foreach (var item in values)
            {
                token.ThrowIfCancellationRequested();
                frequencies.TryGetValue(item, out var count);
                frequencies[item] = checked(count + 1);
            }
        }

        return frequencies.TryGetValue(value, out var frequency) ? frequency : 0;
    }

    /// <summary>Computes a shifted Welford mean and population standard deviation once.</summary>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>Finite population statistics.</returns>
    internal (double Mean, double Sigma) Statistics(CancellationToken token)
    {
        if (statistics is { } cached)
        {
            return cached;
        }

        PrepareNumbers(false, token);
        long count = 0;
        double mean = 0;
        double m2 = 0;
        var offset = Numbers.Count == 0 ? 0 : Numbers[0];
        foreach (var number in Numbers)
        {
            token.ThrowIfCancellationRequested();
            count++;
            var centered = number - offset;
            var delta = centered - mean;
            mean += delta / count;
            m2 += delta * (centered - mean);
        }

        var result = (Mean: mean + offset, Sigma: count == 0 ? 0 : Math.Sqrt(m2 / count));
        if (!Finite(result.Mean) || !Finite(result.Sigma))
        {
            throw new ConditionalFormattingException("ConditionalFormattingPopulationInvalid", "Nonfinite mean or population standard deviation.");
        }

        statistics = result;
        return result;
    }

    private sealed class ValueComparer : IEqualityComparer<FormulaValue>
    {
        public bool Equals(FormulaValue left, FormulaValue right) => left.Kind == right.Kind && left.Kind switch
        {
            FormulaValueKind.Number => left.Number == right.Number,
            FormulaValueKind.Boolean => left.Boolean == right.Boolean,
            FormulaValueKind.Text or FormulaValueKind.Error => StringComparer.OrdinalIgnoreCase.Equals(left.Text, right.Text),
            _ => true,
        };

        public int GetHashCode(FormulaValue value) => value.Kind switch
        {
            FormulaValueKind.Number => HashCode.Combine(value.Kind, value.Number),
            FormulaValueKind.Boolean => HashCode.Combine(value.Kind, value.Boolean),
            FormulaValueKind.Text or FormulaValueKind.Error => HashCode.Combine(value.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(value.Text)),
            _ => 0,
        };
    }
}
