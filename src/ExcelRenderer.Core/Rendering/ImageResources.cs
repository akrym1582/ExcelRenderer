using System.Runtime.CompilerServices;

namespace ExcelRenderer.Core.Rendering;

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
    private ConditionalWeakTable<byte[], DecodeFailure> failures = new();
    private int failureCount;
    private ConditionalWeakTable<byte[], AlphaInfo> alpha = new();
    private int alphaCount;

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
        failures = new();
        failureCount = 0;
        alpha = new();
        alphaCount = 0;
        CurrentSlot.Value = previous;
    }

    /// <summary>Reuses a bounded, weak-key alpha classification across resource eviction and page passes.</summary>
    /// <param name="bytes">The original immutable image snapshot, using the decoder's reference identity.</param>
    /// <param name="image">The currently leased validated pixels.</param>
    /// <param name="token">The conversion token, checked on every scanned row.</param>
    /// <returns>The alpha class without retaining any additional native image.</returns>
    internal ImageAlphaKind InspectAlpha(byte[] bytes, DecodedImage image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (alpha.TryGetValue(bytes, out var cached))
        {
            ConversionMetrics.Report("imageAlphaCacheHits", 1);
            return cached.Kind;
        }

        var kind = Inspect();
        if (alphaCount >= 512)
        {
            alpha = new();
            alphaCount = 0;
        }

        alpha.Add(bytes, new(kind));
        alphaCount++;
        return kind;

        ImageAlphaKind Inspect()
        {
            if (image.Raster.AlphaType == SkiaSharp.SKAlphaType.Opaque)
            {
                return ImageAlphaKind.Opaque;
            }

            ConversionMetrics.Report("imageAlphaScans", 1);
            using var bitmap = SkiaSharp.SKBitmap.FromImage(image.Raster);
            var visible = false;
            var transparent = false;
            for (var y = 0; y < bitmap.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var value = bitmap.GetPixel(x, y).Alpha;
                    if (value is > 0 and < 255)
                    {
                        return ImageAlphaKind.Partial;
                    }

                    visible |= value != 0;
                    transparent |= value == 0;
                }
            }

            return !visible ? ImageAlphaKind.Transparent : transparent ? ImageAlphaKind.BinaryMask : ImageAlphaKind.Opaque;
        }
    }

    /// <summary>Shares one complete pixel decode and its actual failure across drawing passes.</summary>
    /// <param name="bytes">The borrowed encoded image.</param>
    /// <param name="failure">The original failure. Callers still report each location.</param>
    /// <returns>A bounded lease of the validated raster/vector representations.</returns>
    internal Lease<DecodedImage>? AcquireImage(byte[] bytes, out string? failure)
    {
        if (failures.TryGetValue(bytes, out var cached))
        {
            failure = cached.Reason;
            return null;
        }

        string? reason = null;
        var lease = Acquire(bytes, () =>
        {
            var image = ImageDecoder.Decode(bytes, out reason);
            return (image, image?.EstimatedBytes ?? 0);
        });
        failure = reason;
        if (lease is null)
        {
            if (failureCount >= 512)
            {
                failures = new();
                failureCount = 0;
            }

            failures.Add(bytes, new(reason ?? "Image decoder returned no pixels."));
            failureCount++;
        }

        return lease;
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
            // The image-specific wrapper caches only reasons; each caller reports its own location.
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

    private sealed record DecodeFailure(string Reason);

    private sealed record AlphaInfo(ImageAlphaKind Kind);

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
