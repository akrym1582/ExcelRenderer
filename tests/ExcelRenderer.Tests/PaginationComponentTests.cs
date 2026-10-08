using ExcelRenderer.Layout;
using ExcelRenderer.Model;

using Xunit;
namespace ExcelRenderer.Tests;

/// <summary>ページ帯の分割境界と、繰り返しタイトル・本文の座標対応を検証します。</summary>
public sealed class PaginationComponentTests
{
    /// <summary>手動改ページと繰り返しタイトルの占有幅を指定してページ帯を構築し、分割境界が 20 ポイントになることを検証します。</summary>
    [Fact(DisplayName = "手動改ページと繰り返しタイトルの占有幅を指定してページ帯を構築し、分割境界が 20 ポイントになる")]
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

    /// <summary>倍率 2 と余白を指定したページで、タイトルと本文を別の座標規則で配置し、本文クリップ領域も期待値に一致することを検証します。</summary>
    [Fact(DisplayName = "倍率 2 と余白を指定したページで、タイトルと本文を別の座標規則で配置し、本文クリップ領域も期待値に一致する")]
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
