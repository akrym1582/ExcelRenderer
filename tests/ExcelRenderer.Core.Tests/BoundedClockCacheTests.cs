using ExcelRenderer.Core.Rendering;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class BoundedClockCacheTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(512)]
    public void Full_cache_reuses_evicted_slots_with_amortized_linear_scans(int capacity)
    {
        var cache = new BoundedClockCache<int, int>("testCache", capacity, 131072);
        var scans = 0d;
        ConversionMetrics.Observer = (name, value) => { if (name == "testCacheScans") scans += value; };
        try
        {
            for (var index = 0; index < capacity; index++) cache.Store(index, index, 1);
            Assert.Equal(capacity, scans);
            for (var index = capacity; index < capacity + 1000; index++) cache.Store(index, index, 1);
            Assert.Equal(capacity, cache.Count);
            Assert.InRange(scans, 1000, 2 * (capacity + 1000));
            for (var index = 1000; index < capacity + 1000; index++)
            {
                Assert.True(cache.TryGetValue(index, out var value));
                Assert.Equal(index, value);
            }
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void Replacement_and_multiple_budget_evictions_reuse_released_slots()
    {
        var cache = new BoundedClockCache<int, int>("testCache", 4, 8);
        for (var index = 0; index < 4; index++) cache.Store(index, index, 2);
        cache.Store(2, 20, 2);
        Assert.Equal(4, cache.Count);
        Assert.Equal(8, cache.RetainedUnits);
        Assert.True(cache.TryGetValue(2, out var replacement));
        Assert.Equal(20, replacement);
        cache.Store(4, 40, 7);
        Assert.Equal(1, cache.Count);
        Assert.Equal(7, cache.RetainedUnits);
        Assert.True(cache.TryGetValue(4, out var last));
        Assert.Equal(40, last);
        cache.Store(5, 50, 1);
        Assert.Equal(2, cache.Count);
        Assert.Equal(8, cache.RetainedUnits);
    }

    [Fact]
    public void Budget_bypasses_oversized_values_and_replaces_expired_weak_slots()
    {
        var cache = new BoundedClockCache<string, object>("testCache", 3, 10);
        var original = new object();
        cache.Store("first", original, 4);
        cache.Store("second", new(), 4);
        cache.Store("oversized", new(), 11);
        Assert.Equal(2, cache.Count);
        Assert.Equal(8, cache.RetainedUnits);
        Assert.False(cache.TryGetValue("oversized", out _));
        var replacement = new object();
        cache.Store("first", replacement, 2);
        Assert.True(cache.TryGetValue("first", out var found));
        Assert.Same(replacement, found);
        Assert.Equal(6, cache.RetainedUnits);
        cache.Store("third", new(), 5);
        Assert.InRange(cache.Count, 1, 3);
        Assert.InRange(cache.RetainedUnits, 5, 10);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.RetainedUnits);
        Assert.False(cache.TryGetValue("first", out _));
        cache.Store("after-clear", original, 10);
        Assert.True(cache.TryGetValue("after-clear", out found));
        Assert.Same(original, found);
    }

    [Fact]
    public void Mixed_hot_and_unique_keys_keep_the_hot_value_without_full_clear()
    {
        var cache = new BoundedClockCache<int, object>("testCache", 8, 100);
        var hot = new object();
        cache.Store(-1, hot, 1);
        var hits = 0;
        for (var index = 0; index < 1000; index++)
        {
            if (cache.TryGetValue(-1, out var found))
            {
                Assert.Same(hot, found);
                hits++;
            }
            else
            {
                cache.Store(-1, hot, 1);
            }

            cache.Store(index, new(), 1);
            Assert.InRange(cache.Count, 1, 8);
        }

        Assert.True(hits > 800);
    }
}
