using System.Collections;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Projects immutable model dictionaries while preserving source enumeration order.</summary>
/// <typeparam name="TSourceKey">The source key type.</typeparam>
/// <typeparam name="TSourceValue">The source value type.</typeparam>
/// <typeparam name="TKey">The projected key type.</typeparam>
/// <typeparam name="TValue">The projected value type.</typeparam>
internal sealed class CoreDictionaryView<TSourceKey, TSourceValue, TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TSourceKey : notnull
    where TKey : notnull
{
    private readonly IReadOnlyDictionary<TSourceKey, TSourceValue> source;
    private readonly Func<TSourceKey, TKey> key;
    private readonly Func<TKey, TSourceKey> sourceKey;
    private readonly Func<TSourceValue, TValue> value;

    /// <summary>Initializes a new instance of the <see cref="CoreDictionaryView{TSourceKey, TSourceValue, TKey, TValue}"/> class.</summary>
    /// <param name="source">The owned immutable dictionary.</param>
    /// <param name="key">The key projection.</param>
    /// <param name="sourceKey">The reverse key projection.</param>
    /// <param name="value">The value projection, with style-local caching when needed.</param>
    internal CoreDictionaryView(IReadOnlyDictionary<TSourceKey, TSourceValue> source, Func<TSourceKey, TKey> key, Func<TKey, TSourceKey> sourceKey, Func<TSourceValue, TValue> value)
    {
        this.source = source;
        this.key = key;
        this.sourceKey = sourceKey;
        this.value = value;
    }

    /// <inheritdoc/>
    public int Count => source.Count;

    /// <inheritdoc/>
    public IEnumerable<TKey> Keys => source.Keys.Select(key);

    /// <inheritdoc/>
    public IEnumerable<TValue> Values => source.Values.Select(value);

    /// <summary>Gets the original dictionary for reverse projection at an internal boundary.</summary>
    internal IReadOnlyDictionary<TSourceKey, TSourceValue> Source => source;

    /// <inheritdoc/>
    public TValue this[TKey key] => value(source[sourceKey(key)]);

    /// <inheritdoc/>
    public bool ContainsKey(TKey key) => source.ContainsKey(sourceKey(key));

    /// <inheritdoc/>
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (source.TryGetValue(sourceKey(key), out var entry))
        {
            value = this.value(entry);
            return true;
        }

        value = default!;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => source.Select(entry => new KeyValuePair<TKey, TValue>(key(entry.Key), value(entry.Value))).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Applies selection to original values before their public projection.</summary>
    /// <param name="predicate">The source selection.</param>
    /// <returns>A view borrowing selected neutral values.</returns>
    internal CoreDictionaryView<TSourceKey, TSourceValue, TKey, TValue> Filter(Func<TSourceKey, TSourceValue, bool> predicate) => new(new FilteredDictionaryView<TSourceKey, TSourceValue>(source, predicate), key, sourceKey, value);
}
