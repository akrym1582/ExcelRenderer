using ExcelRenderer.Core.Rendering;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class BoundedClockCacheTests
{
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
