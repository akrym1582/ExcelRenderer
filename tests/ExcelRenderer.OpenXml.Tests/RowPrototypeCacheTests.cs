using System.Xml.Linq;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class RowPrototypeCacheTests
{
    [Fact]
    public void DeepCopiesProtectPrototypesAcrossInterleavedRepeatedRows()
    {
        var cache = new RowPrototypeCache();
        for (var row = 1; row <= 4; row++)
        {
            var copy = cache.Store(row, 100, XElement.Parse($"<row r='{row}'><c><v>original{row}</v></c></row>"));
            copy.Element("c")!.Element("v")!.Value = "edited";
        }

        for (var repeat = 0; repeat < 10; repeat++)
        for (var row = 1; row <= 4; row++)
        {
            var copy = cache.Clone(row)!;
            Assert.Equal("original" + row, copy.Element("c")!.Value);
            copy.RemoveAll();
        }
        Assert.Equal(4, cache.Count);
    }

    [Fact]
    public void EntryByteAndIndividualRowLimitsBoundRetentionAndEvictLeastRecentlyUsed()
    {
        var cache = new RowPrototypeCache();
        for (var row = 1; row <= RowPrototypeCache.MaximumEntries; row++) cache.Store(row, 1, new XElement("row"));
        cache.Clone(1);
        cache.Store(65, 1, new XElement("row"));
        Assert.NotNull(cache.Clone(1));
        Assert.Null(cache.Clone(2));
        Assert.Equal(RowPrototypeCache.MaximumEntries, cache.Count);

        for (var row = 100; row < 110; row++)
        {
            cache.Store(row, RowPrototypeCache.MaximumRowBytes, new XElement("row"));
            Assert.InRange(cache.SourceBytes, 0, RowPrototypeCache.MaximumBytes);
        }
        Assert.Equal(4, cache.Count);
        var large = new XElement("row", new string('x', RowPrototypeCache.MaximumRowBytes));
        Assert.Same(large, cache.Store(200, RowPrototypeCache.MaximumRowBytes + 1, large));
        Assert.Null(cache.Clone(200));
        Assert.Equal(4, cache.Count);
    }
}
