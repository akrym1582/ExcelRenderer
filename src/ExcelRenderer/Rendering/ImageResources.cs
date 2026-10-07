namespace ExcelRenderer.Rendering;

/// <summary>Conversion-owned native image resources, bounded by decoded pixel estimates.</summary>
internal sealed class ImageResources : IDisposable
{
    /// <summary>The default decoded resource estimate limit in bytes.</summary>
    internal const long DefaultLimit = 64 * 1024 * 1024;
    private static readonly AsyncLocal<ImageResources?> CurrentSlot = new();
    private readonly ImageResources? previous;
    private readonly long limit;
    private readonly Dictionary<(byte[] Bytes, Type Kind), Entry> entries = new();
    private readonly LinkedList<Entry> lru = new();
    private long estimatedBytes;

    /// <summary>Initializes a new instance of the <see cref="ImageResources"/> class.</summary>
    /// <param name="limit">The limit used by this operation.</param>
    internal ImageResources(long limit = DefaultLimit)
    {
        this.limit = limit;
        previous = CurrentSlot.Value;
        CurrentSlot.Value = this;
    }

    /// <summary>Gets the image-resource owner in the current conversion scope.</summary>
    internal static ImageResources? Current => CurrentSlot.Value;

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var entry in entries.Values)
        {
            entry.Retired = true;
            if (entry.Active == 0)
            {
                entry.Value.Dispose();
            }
        }

        entries.Clear();
        lru.Clear();
        estimatedBytes = 0;
        CurrentSlot.Value = previous;
    }

    /// <summary>Acquires a lease from the bounded native image cache.</summary>
    /// <typeparam name="T">The native resource type.</typeparam>
    /// <param name="bytes">The bytes used by this operation.</param>
    /// <param name="create">The create used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal Lease<T>? Acquire<T>(byte[] bytes, Func<(T? Value, long Bytes)> create)
        where T : class, IDisposable
    {
        var key = (bytes, typeof(T));
        if (entries.TryGetValue(key, out var cached))
        {
            lru.Remove(cached.Node!);
            cached.Node = lru.AddLast(cached);
            cached.Active++;
            ConversionMetrics.Report("imageCacheHit", 1);
            return new((T)cached.Value, () => Release(cached));
        }

        ConversionMetrics.Report("imageDecode", 1);
        var (value, size) = create();
        if (value is null)
        {
            // Do not cache failures: each occurrence retains the existing diagnostic behavior.
            return null;
        }

        if (size > limit)
        {
            return new(value, value.Dispose);
        }

        for (var node = lru.First; size > limit - estimatedBytes && node is not null;)
        {
            var next = node.Next;
            var entry = node.Value;
            if (entry.Active == 0)
            {
                lru.Remove(node);
                entries.Remove(entry.Key);
                estimatedBytes -= entry.Bytes;
                entry.Value.Dispose();
            }

            node = next;
        }

        if (size > limit - estimatedBytes)
        {
            return new(value, value.Dispose);
        }

        var added = new Entry(key, value, size) { Active = 1 };
        added.Node = lru.AddLast(added);
        entries.Add(key, added);
        estimatedBytes += size;
        ConversionMetrics.Report("imageCacheEstimatedBytes", estimatedBytes);
        return new(value, () => Release(added));
    }

    private static void Release(Entry entry)
    {
        entry.Active--;
        if (entry.Retired && entry.Active == 0)
        {
            entry.Value.Dispose();
        }
    }

    /// <summary>Pins a cached native resource while it is being drawn.</summary>
    /// <typeparam name="T">The native resource type.</typeparam>
    internal sealed class Lease<T> : IDisposable
        where T : class, IDisposable
    {
        private Action? release;

        /// <summary>Initializes a new instance of the <see cref="Lease{T}"/> class.</summary>
        /// <param name="value">The value used by this operation.</param>
        /// <param name="release">The release used by this operation.</param>
        internal Lease(T value, Action release)
        {
            Value = value;
            this.release = release;
        }

        /// <summary>Gets the resource protected by this lease.</summary>
        internal T Value { get; }

        /// <inheritdoc/>
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }

    private sealed class Entry
    {
        internal Entry((byte[] Bytes, Type Kind) key, IDisposable value, long bytes)
        {
            Key = key;
            Value = value;
            Bytes = bytes;
        }

        internal (byte[] Bytes, Type Kind) Key { get; }

        internal IDisposable Value { get; }

        internal long Bytes { get; }

        internal int Active { get; set; }

        internal bool Retired { get; set; }

        internal LinkedListNode<Entry>? Node { get; set; }
    }
}
