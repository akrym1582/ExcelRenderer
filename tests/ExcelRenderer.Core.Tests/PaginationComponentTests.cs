using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

using Xunit;
namespace ExcelRenderer.Core.Tests;

public sealed class PaginationComponentTests
{
    [Fact]
    public void PageBandBuilderPreservesManualBreaksAndRepeatedTitleCapacity()
    {
        var bands = PageBandBuilder.Create(
            [0, 1, 2, 3],
            index => index * 10,
            index => (index + 1) * 10,
            25,
            (_, end) => end,
            repeatedEnd: 10,
            repeatedSize: 5,
            manualBreaks: [1]);

        Assert.Equal([new PageBand(0, 20), new PageBand(20, double.PositiveInfinity)], bands);
    }

    [Fact]
    public void PagePlacementUsesDistinctTitleAndBodyMappings()
    {
        var settings = new PageSettings(100, 100)
        {
            MarginLeft = 10,
            MarginTop = 10,
            MarginRight = 10,
            MarginBottom = 10,
        };
        var placement = new PagePlacement(settings, new(20, 50), new(20, 50), 50, 50, 10, 10, 2);

        Assert.Equal(10, placement.MapCellX(0, repeatsTitles: true, isTitle: true, titleStart: 0));
        Assert.Equal(30, placement.MapCellX(20, repeatsTitles: true, isTitle: false, titleStart: 0));
        Assert.Equal(new ReportRect(30, 30, 10, 10), placement.MapBodyObjectBounds(new(20, 20, 5, 5)));
        Assert.Equal(new ReportRect(30, 30, 60, 60), placement.BodyClip);
    }
}
