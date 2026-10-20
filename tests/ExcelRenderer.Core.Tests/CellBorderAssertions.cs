using Xunit;

namespace ExcelRenderer.Core.Tests;

/// <summary>Checks complete saved border coverage independently of stroke subdivision.</summary>
internal static class CellBorderAssertions
{
    internal static void StackedCells(PdfContentProbe probe)
    {
        // The ARGB fixture has five filled cells immediately left of five bordered cells.
        var fills = probe.Paints.Where(paint => paint.Operation == "f").ToArray();
        Assert.Equal(5, fills.Length);
        var left = fills[0].Points.Max(point => point.X);
        var right = left + left - fills[0].Points.Min(point => point.X);
        var rows = fills.SelectMany(paint => paint.Points.Select(point => Math.Round(point.Y, 3))).Distinct().Order().ToArray();
        Assert.Equal(6, rows.Length);
        var horizontal = new HashSet<int>();
        var vertical = new HashSet<(bool Right, int Row)>();
        foreach (var paint in probe.Paints.Where(paint => paint.Operation == "S"))
        {
            Assert.True(paint.Points.Length >= 2);
            foreach (var (start, end) in paint.Points.Zip(paint.Points.Skip(1)))
            {
                var x0 = Math.Round(start.X, 3);
                var x1 = Math.Round(end.X, 3);
                var y0 = Array.IndexOf(rows, Math.Round(start.Y, 3));
                var y1 = Array.IndexOf(rows, Math.Round(end.Y, 3));
                Assert.InRange(y0, 0, 5);
                Assert.InRange(y1, 0, 5);
                if (y0 == y1)
                {
                    Assert.Equal(Math.Round(left, 3), Math.Min(x0, x1));
                    Assert.Equal(Math.Round(right, 3), Math.Max(x0, x1));
                    horizontal.Add(y0);
                }
                else
                {
                    Assert.Equal(x0, x1);
                    Assert.True(x0 == Math.Round(left, 3) || x0 == Math.Round(right, 3));
                    for (var row = Math.Min(y0, y1); row < Math.Max(y0, y1); row++)
                    {
                        vertical.Add((x0 == Math.Round(right, 3), row));
                    }
                }
            }
        }

        Assert.Equal(Enumerable.Range(0, 6), horizontal.Order());
        Assert.Equal(10, vertical.Count);
    }
}
