using ExcelRenderer.Tool.Commands;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed class MetricSummaryTests
{
    [Theory]
    [InlineData("reader.ms")]
    [InlineData("imageDecode")]
    [InlineData("commands")]
    [InlineData("typefaceCreated")]
    [InlineData("fontsBytesLoaded")]
    [InlineData("svgBufferBytes")]
    [InlineData("textWidthCacheHits")]
    [InlineData("richTextLayoutCacheMisses")]
    [InlineData("layoutCacheScans")]
    [InlineData("svg.spillCount")]
    public void Repeated_events_and_durations_are_accumulated(string name)
    {
        var summary = new MetricSummary();
        summary.Report(name, 2);
        summary.Report(name, 3);
        Assert.Equal(5, summary.Snapshot().Single().Value);
    }

    [Theory]
    [InlineData("imageCacheEstimatedBytes")]
    [InlineData("pngBitmapBytes")]
    [InlineData("layoutCacheEntries")]
    [InlineData("svgBufferMemoryPeakBytes")]
    [InlineData("textWidthCacheRetainedUnits.max")]
    public void Decreasing_resource_observations_preserve_the_peak(string name)
    {
        var summary = new MetricSummary();
        summary.Report(name, 100);
        summary.Report(name, 20);
        Assert.Equal(100, summary.Snapshot().Single().Value);
    }

    [Theory]
    [InlineData("renderCells")]
    [InlineData("pdfBackend")]
    [InlineData("readerStyleCacheHits")]
    [InlineData("conditionalFormatting.evaluations")]
    [InlineData("textWidthCacheEntries.current")]
    [InlineData("managedAllocatedBytes")]
    [InlineData("unknownFutureSnapshot")]
    public void Producer_snapshots_are_not_double_counted(string name)
    {
        var summary = new MetricSummary();
        summary.Report(name, 100);
        summary.Report(name, 20);
        Assert.Equal(20, summary.Snapshot().Single().Value);
    }

    [Fact]
    public void Payload_deltas_produce_current_count_and_preserve_peak_after_release()
    {
        var summary = new MetricSummary();
        foreach (var value in new[] { 1, 1, -1, -1 })
        {
            summary.Report("pagePayloadActive", value);
        }

        summary.Report("pagePayloadMax", 1);
        var values = summary.Snapshot().ToDictionary();
        Assert.Equal(0, values["pagePayloadActive"]);
        Assert.Equal(2, values["pagePayloadMax"]);
    }

    [Fact]
    public void Concurrent_reports_are_counted_and_output_is_stable_and_sorted()
    {
        var summary = new MetricSummary();
        Parallel.For(0, 1000, _ => summary.Report("imageDecode", 1));
        var snapshot = summary.Snapshot();
        summary.Report("buffer.spillCount", 1);
        Assert.Equal(1000, snapshot.Single().Value);
        Assert.Equal(new[] { "buffer.spillCount", "imageDecode" }, summary.Snapshot().Select(item => item.Key));
    }
}
