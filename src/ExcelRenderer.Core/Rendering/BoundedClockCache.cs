namespace ExcelRenderer.Core.Rendering;

/// <summary>Bounds retained entries and caller-accounted payload units with second-chance eviction.</summary>
/// <typeparam name="TKey">The immutable lookup key.</typeparam>
/// <typeparam name="TValue">The cached value.</typeparam>
internal sealed class BoundedClockCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> entries = new();
    private readonly int maximumEntries;
    private readonly int maximumUnits;
    private readonly string metric;
    private Entry?[]? slots;
    private int hand;

    /// <summary>Initializes a new instance of the <see cref="BoundedClockCache{TKey, TValue}"/> class.</summary>
    /// <param name="metric">The diagnostic metric prefix.</param>
    /// <param name="maximumEntries">The retained entry limit.</param>
    /// <param name="maximumUnits">The retained payload limit.</param>
    internal BoundedClockCache(string metric, int maximumEntries = 512, int maximumUnits = 131072)
    {
        this.metric = metric;
        this.maximumUnits = maximumUnits;
        this.maximumEntries = maximumEntries;
    }

    /// <summary>Gets the number of retained entries.</summary>
    internal int Count => entries.Count;

    /// <summary>Gets the conservatively accounted retained payload.</summary>
    internal int RetainedUnits { get; private set; }

    /// <summary>Finds a value and gives its slot a second chance.</summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The cached value, or the default on a miss.</param>
    /// <returns>Whether the key was found.</returns>
    internal bool TryGetValue(TKey key, out TValue value)
    {
        if (entries.TryGetValue(key, out var entry))
        {
            entry.Referenced = true;
            value = entry.Value;
            Report("Hits", 1);
            return true;
        }

        value = default!;
        Report("Misses", 1);
        return false;
    }

    /// <summary>Admits a bounded value, evicting individual cold entries when necessary.</summary>
    /// <param name="key">The immutable key.</param>
    /// <param name="value">The value.</param>
    /// <param name="units">The retained key and value payload units.</param>
    internal void Store(TKey key, TValue value, long units)
    {
        if (units < 0 || units > maximumUnits)
        {
            Report("Bypasses", 1);
            return;
        }

        var slots = this.slots ??= new Entry[maximumEntries];
        var available = -1;

        // Existing weak values can expire; replace their entry without clearing other slots.
        if (entries.TryGetValue(key, out var previous))
        {
            entries.Remove(key);
            RetainedUnits -= previous.Units;
            slots[previous.Slot] = null;
            available = previous.Slot;
        }

        while (entries.Count >= slots.Length || RetainedUnits + units > maximumUnits)
        {
            var entry = slots[hand];
            Report("Scans", 1);
            if (entry is not null)
            {
                if (entry.Referenced)
                {
                    entry.Referenced = false;
                }
                else
                {
                    entries.Remove(entry.Key);
                    RetainedUnits -= entry.Units;
                    slots[hand] = null;
                    available = hand;
                    Report("Evictions", 1);
                    if (metric == "layoutCache")
                    {
                        ConversionMetrics.Report("layoutCacheEviction", 1);
                    }
                }
            }

            hand = (hand + 1) % slots.Length;
        }

        // Reuse the slot just released by replacement or eviction. Advancing the
        // clock and searching again would scan the entire ring at capacity.
        if (available < 0)
        {
            while (slots[hand] is not null)
            {
                Report("Scans", 1);
                hand = (hand + 1) % slots.Length;
            }

            Report("Scans", 1);
            available = hand;
        }

        var added = new Entry(key, value, (int)units, available);
        slots[available] = added;
        entries.Add(key, added);
        RetainedUnits += added.Units;
        hand = (available + 1) % slots.Length;
        if (metric == "layoutCache")
        {
            ConversionMetrics.Report("layoutCacheEntries", Count);
            ConversionMetrics.Report("layoutCacheRetainedUnits", RetainedUnits);
        }

        Report("Entries.current", Count);
        Report("RetainedUnits.current", RetainedUnits);
        Report("RetainedUnits.max", RetainedUnits);
    }

    /// <summary>Releases all values after a resolver generation change.</summary>
    internal void Clear()
    {
        entries.Clear();
        if (slots is not null)
        {
            Array.Clear(slots, 0, slots.Length);
        }

        hand = 0;
        RetainedUnits = 0;
        Report("Entries.current", 0);
        Report("RetainedUnits.current", 0);
    }

    private void Report(string suffix, double value)
    {
        if (ConversionMetrics.Observer is not null)
        {
            ConversionMetrics.Report(metric + suffix, value);
        }
    }

    private sealed class Entry(TKey key, TValue value, int units, int slot)
    {
        internal TKey Key { get; } = key;

        internal TValue Value { get; } = value;

        internal int Units { get; } = units;

        internal int Slot { get; } = slot;

        internal bool Referenced { get; set; } = true;
    }
}
