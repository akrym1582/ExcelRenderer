namespace ExcelRenderer.Core.Layout;

/// <summary>Indexes half-open intervals without expanding their covered coordinates.</summary>
/// <typeparam name="T">The indexed value type.</typeparam>
internal sealed class BandIndex<T>
{
    private readonly (T Value, double Start, double End)[] _entries;
    private readonly double[] _maximumEnds;

    /// <summary>Initializes a new instance of the <see cref="BandIndex{T}"/> class.</summary>
    /// <param name="entries">Values with their interval bounds.</param>
    internal BandIndex(IEnumerable<(T Value, double Start, double End)> entries)
    {
        _entries = entries.OrderBy(entry => entry.Start).ToArray();
        _maximumEnds = new double[_entries.Length];
        BuildMaximumEnds(0, _entries.Length);
    }

    /// <summary>Finds values whose interval starts lie in a half-open band.</summary>
    /// <param name="start">The inclusive band start.</param>
    /// <param name="end">The exclusive band end.</param>
    /// <returns>The values with matching starts.</returns>
    internal IEnumerable<T> QueryStarts(double start, double end)
    {
        var low = 0;
        var high = _entries.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_entries[middle].Start < start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var i = low; i < _entries.Length && _entries[i].Start < end; i++)
        {
            yield return _entries[i].Value;
        }
    }

    /// <summary>Finds intervals intersecting a half-open band.</summary>
    /// <param name="start">The inclusive band start.</param>
    /// <param name="end">The exclusive band end.</param>
    /// <returns>The intersecting values.</returns>
    internal IEnumerable<T> Query(double start, double end)
    {
        if (start >= end)
        {
            yield break;
        }

        foreach (var value in QueryRange(0, _entries.Length, start, end))
        {
            yield return value;
        }
    }

    private double BuildMaximumEnds(int low, int high)
    {
        if (low >= high)
        {
            return double.NegativeInfinity;
        }

        var middle = low + ((high - low) / 2);
        return _maximumEnds[middle] = Math.Max(
            _entries[middle].End,
            Math.Max(BuildMaximumEnds(low, middle), BuildMaximumEnds(middle + 1, high)));
    }

    private IEnumerable<T> QueryRange(int low, int high, double start, double end)
    {
        if (low >= high || _entries[low].Start >= end)
        {
            yield break;
        }

        var middle = low + ((high - low) / 2);
        if (_maximumEnds[middle] <= start)
        {
            yield break;
        }

        foreach (var value in QueryRange(low, middle, start, end))
        {
            yield return value;
        }

        if (_entries[middle].Start < end && _entries[middle].End > start)
        {
            yield return _entries[middle].Value;
        }

        foreach (var value in QueryRange(middle + 1, high, start, end))
        {
            yield return value;
        }
    }
}
