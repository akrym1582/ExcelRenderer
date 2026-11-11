using ExcelRenderer.Core.Layout;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class PageBandBuilderTests
{
    /// <summary>The explicit legacy mode retains historical oversized-merge bands for frozen output compatibility.</summary>
    [Fact]
    public void Legacy_mode_preserves_oversized_merge_bands()
    {
        var bands = PageBandBuilder.Create(
            [1, 2, 3, 4],
            column => (column - 1) * 10,
            column => column * 10,
            20,
            (column, end) => column == 2 ? 40 : end,
            splitOversizedMerges: false);

        Assert.Equal(3, bands.Count);
        Assert.Equal(new PageBand(0, 10), bands[0]);
        Assert.Equal(new PageBand(10, 20), bands[1]);
    }

    /// <summary>A merge wider than the paper must split without pushing preceding blank columns onto another page.</summary>
    [Fact]
    public void Oversized_merge_splits_at_column_boundaries_without_extra_leading_bands()
    {
        var bands = PageBandBuilder.Create(
            [1, 2, 3, 4],
            column => (column - 1) * 10,
            column => column * 10,
            20,
            (column, end) => column == 2 ? 40 : end);

        Assert.Equal(2, bands.Count);
        Assert.Equal(new PageBand(0, 20), bands[0]);
        Assert.Equal(new PageBand(20, double.PositiveInfinity), bands[1]);
    }

    /// <summary>A merge that fits a whole page still moves together when insufficient space remains.</summary>
    [Fact]
    public void Merge_that_fits_one_page_keeps_its_existing_grouping()
    {
        var bands = PageBandBuilder.Create(
            [1, 2, 3, 4],
            column => (column - 1) * 10,
            column => column * 10,
            20,
            (column, end) => column == 2 ? 30 : end);

        Assert.Equal(3, bands.Count);
        Assert.Equal(new PageBand(0, 10), bands[0]);
        Assert.Equal(new PageBand(10, 30), bands[1]);
        Assert.Equal(new PageBand(30, double.PositiveInfinity), bands[2]);
    }
}
