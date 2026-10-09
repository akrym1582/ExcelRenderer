using System.Collections;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Filters borrowed dictionaries without duplicating their values or enumeration order.</summary>
/// <typeparam name="TKey">The original key.</typeparam>
/// <typeparam name="TValue">The borrowed value.</typeparam>
internal sealed class FilteredDictionaryView<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly IReadOnlyDictionary<TKey, TValue> source;
    private readonly Func<TKey, TValue, bool> predicate;

    /// <summary>Initializes a new instance of the <see cref="FilteredDictionaryView{TKey, TValue}"/> class.</summary>
    /// <param name="source">The original immutable dictionary.</param>
    /// <param name="predicate">The selection predicate.</param>
    internal FilteredDictionaryView(IReadOnlyDictionary<TKey, TValue> source, Func<TKey, TValue, bool> predicate)
    {
        this.source = source;
        this.predicate = predicate;
    }

    /// <inheritdoc/>
    public int Count => source.Count(pair => predicate(pair.Key, pair.Value));

    /// <inheritdoc/>
    public IEnumerable<TKey> Keys => this.Select(pair => pair.Key);

    /// <inheritdoc/>
    public IEnumerable<TValue> Values => this.Select(pair => pair.Value);

    /// <inheritdoc/>
    public TValue this[TKey key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();

    /// <inheritdoc/>
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    /// <inheritdoc/>
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (source.TryGetValue(key, out var entry) && predicate(key, entry))
        {
            value = entry;
            return true;
        }

        value = default!;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => source.Where(pair => predicate(pair.Key, pair.Value)).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
