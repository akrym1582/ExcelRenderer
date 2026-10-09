namespace ExcelRenderer.Core.Rendering;

/// <summary>Aggregates product diagnostics with stable ordering and bounded retention.</summary>
/// <typeparam name="T">The product diagnostic value.</typeparam>
internal sealed class DiagnosticStore<T>
{
    private readonly Dictionary<string, T> values = new(StringComparer.Ordinal);
    private readonly int maximum;
    private readonly Func<T, T, T> merge;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticStore{T}"/> class.</summary>
    /// <param name="maximum">The maximum retained unique values.</param>
    /// <param name="merge">The occurrence merger.</param>
    internal DiagnosticStore(int maximum, Func<T, T, T> merge)
    {
        this.maximum = maximum;
        this.merge = merge;
    }

    /// <summary>Gets the number of discarded occurrences.</summary>
    internal int Discarded { get; private set; }

    /// <summary>Gets retained values in their original order.</summary>
    internal IEnumerable<T> Values => values.Values;

    /// <summary>Adds a diagnostic using the product's explicit identity and suppression policy.</summary>
    /// <param name="key">The complete identity.</param>
    /// <param name="value">The diagnostic.</param>
    /// <param name="suppressed">Whether retention is suppressed.</param>
    internal void Add(string key, T value, bool suppressed = false)
    {
        if (values.TryGetValue(key, out var existing))
        {
            values[key] = merge(existing, value);
            return;
        }

        if (values.Count >= maximum)
        {
            Discarded++;
            return;
        }

        if (!suppressed)
        {
            values.Add(key, value);
        }
    }
}
