using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>罫線の線種に応じて描画する線分の座標を求めます。</summary>
internal static class BorderStrokeGeometry
{
    /// <summary>線種に応じた 1 本または複数の描画線分を返します。</summary>
    /// <param name="side">描画する罫線の書式です。</param>
    /// <param name="x1">始点の X 座標です。</param>
    /// <param name="y1">始点の Y 座標です。</param>
    /// <param name="x2">終点の X 座標です。</param>
    /// <param name="y2">終点の Y 座標です。</param>
    /// <param name="inwardX">セル内部を指す X 方向です。</param>
    /// <param name="inwardY">セル内部を指す Y 方向です。</param>
    /// <returns>実際に描画する線分の座標です。</returns>
    internal static IReadOnlyList<(double X1, double Y1, double X2, double Y2)> GetStrokes(
        BorderSide side,
        double x1,
        double y1,
        double x2,
        double y2,
        double inwardX = 0,
        double inwardY = 0)
    {
        if (side.LineStyle == BorderLineStyle.SlantDashDot)
        {
            return GetSlantedDashDots(side, x1, y1, x2, y2);
        }

        if (side.LineStyle != BorderLineStyle.Double)
        {
            return [(x1, y1, x2, y2)];
        }

        if (inwardX == 0 && inwardY == 0)
        {
            var length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
            if (length == 0)
            {
                return [];
            }

            inwardX = -(y2 - y1) / length;
            inwardY = (x2 - x1) / length;
            var distance = Math.Max(side.Width, 0.25);
            return [
                (x1 - (inwardX * distance), y1 - (inwardY * distance), x2 - (inwardX * distance), y2 - (inwardY * distance)),
                (x1 + (inwardX * distance), y1 + (inwardY * distance), x2 + (inwardX * distance), y2 + (inwardY * distance)),
            ];
        }

        var width = Math.Max(side.Width, 0.25);
        return [
            (x1 + (inwardX * width * 0.5), y1 + (inwardY * width * 0.5), x2 + (inwardX * width * 0.5), y2 + (inwardY * width * 0.5)),
            (x1 + (inwardX * width * 3.5), y1 + (inwardY * width * 3.5), x2 + (inwardX * width * 3.5), y2 + (inwardY * width * 3.5)),
        ];
    }

    private static IReadOnlyList<(double X1, double Y1, double X2, double Y2)> GetSlantedDashDots(
        BorderSide side,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        var length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        if (length == 0)
        {
            return [];
        }

        var ux = (x2 - x1) / length;
        var uy = (y2 - y1) / length;
        var nx = -uy;
        var ny = ux;
        var unit = Math.Max(side.Width, 0.5);
        var strokes = new List<(double X1, double Y1, double X2, double Y2)>();
        for (var start = 0d; start < length; start += 9 * unit)
        {
            var dashEnd = Math.Min(start + (4 * unit), length);
            strokes.Add((
                x1 + (ux * start) - (nx * unit * 0.6),
                y1 + (uy * start) - (ny * unit * 0.6),
                x1 + (ux * dashEnd) + (nx * unit * 0.6),
                y1 + (uy * dashEnd) + (ny * unit * 0.6)));

            var dotStart = start + (6 * unit);
            if (dotStart < length)
            {
                var dotEnd = Math.Min(dotStart + (0.5 * unit), length);
                strokes.Add((
                    x1 + (ux * dotStart),
                    y1 + (uy * dotStart),
                    x1 + (ux * dotEnd),
                    y1 + (uy * dotEnd)));
            }
        }

        return strokes;
    }
}
