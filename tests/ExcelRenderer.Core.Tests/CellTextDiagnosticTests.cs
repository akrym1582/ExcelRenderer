using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;
using Xunit;

namespace ExcelRenderer.Core.Tests;

public sealed class CellTextDiagnosticTests
{
    [Theory]
    [InlineData(20, 10, false)]
    [InlineData(20.24, 10.24, false)]
    [InlineData(20.26, 10, true)]
    [InlineData(20, 10.26, true)]
    public void ClippingUsesFinalizedMetricsAndQuarterPointTolerance(double width, double height, bool expected)
    {
        var line = new TextLayoutLine("X", width, height, height - 2, false) { Ascent = height - 2, Descent = 2 };
        var layout = new TextLayoutResult(new(width, height), [line]);
        var style = CellStyle.Default with { WrapText = true, VerticalAlignment = VerticalAlignment.Top };
        Assert.Equal(expected, CellTextDiagnostics.IsClipped(layout, new(0, 0, 20, 10), style));
        Assert.False(CellTextDiagnostics.IsClipped(layout, new(0, 0, 20, 10), style with { TextRotation = 45 }));
        Assert.False(CellTextDiagnostics.IsClipped(layout, new(0, 0, 0, 10), style));
    }
}
